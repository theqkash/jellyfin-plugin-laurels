using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Laurels.Storage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Reviews;

public sealed class ReviewExcerpt
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "tmdb";

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("excerpt")]
    public string Excerpt { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset? CreatedUtc { get; set; }
}

public sealed class TitleReviews
{
    [JsonPropertyName("itemId")]
    public Guid ItemId { get; set; }

    [JsonPropertyName("fetchedUtc")]
    public DateTimeOffset FetchedUtc { get; set; }

    [JsonPropertyName("reviews")]
    public List<ReviewExcerpt> Reviews { get; set; } = [];
}

/// <summary>
/// Short review excerpts from TMDb's public reviews endpoint (needs a TMDb API key or read token).
/// Results are cached for two weeks; the excerpt is the first ~400 characters of each review.
/// </summary>
public sealed class TmdbReviewsClient
{
    private const int ExcerptLength = 400;
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(14);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TmdbReviewsClient> _logger;
    private readonly JsonFileStore<TitleReviews> _store;

    public TmdbReviewsClient(IHttpClientFactory httpClientFactory, ILogger<TmdbReviewsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _store = new JsonFileStore<TitleReviews>(Path.Combine(Plugin.Instance?.CacheFolderPath ?? Path.GetTempPath(), "reviews"), logger);
    }

    public async Task<TitleReviews> GetAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var cached = _store.Get(item.Id.ToString("N"));
        if (cached is not null && cached.FetchedUtc + Ttl > DateTimeOffset.UtcNow)
        {
            return cached;
        }

        var result = new TitleReviews { ItemId = item.Id, FetchedUtc = DateTimeOffset.UtcNow };
        var key = (Plugin.Instance?.Configuration.TmdbApiKey ?? string.Empty).Trim();
        var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
        if (key.Length == 0 || string.IsNullOrWhiteSpace(tmdbId))
        {
            return result;
        }

        var kind = item is Series ? "tv" : "movie";
        var url = $"https://api.themoviedb.org/3/{kind}/{Uri.EscapeDataString(tmdbId)}/reviews?language=en-US&page=1";
        try
        {
            var http = _httpClientFactory.CreateClient("Laurels.Tmdb");
            using var request = new HttpRequestMessage(HttpMethod.Get, key.Length > 60 ? url : url + "&api_key=" + Uri.EscapeDataString(key));
            if (key.Length > 60)
            {
                // Long keys are v4 read access tokens.
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            }

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                await _store.SetAsync(result.ItemId.ToString("N"), result, cancellationToken).ConfigureAwait(false);
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Laurels/TMDb: reviews request failed with {Status} for {Name}", (int)response.StatusCode, item.Name);
                return cached ?? result;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in results.EnumerateArray())
                {
                    var content = r.TryGetProperty("content", out var c) ? c.GetString() : null;
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        continue;
                    }

                    double? rating = null;
                    if (r.TryGetProperty("author_details", out var ad) && ad.TryGetProperty("rating", out var rt) && rt.ValueKind == JsonValueKind.Number)
                    {
                        rating = rt.GetDouble();
                    }

                    DateTimeOffset? created = null;
                    if (r.TryGetProperty("created_at", out var ca) && DateTimeOffset.TryParse(ca.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                    {
                        created = parsed;
                    }

                    result.Reviews.Add(new ReviewExcerpt
                    {
                        Author = r.TryGetProperty("author", out var a) ? a.GetString() : null,
                        Rating = rating,
                        Excerpt = MakeExcerpt(content),
                        Url = r.TryGetProperty("url", out var u) ? u.GetString() : null,
                        CreatedUtc = created,
                    });
                }
            }

            // Rated reviews first, then newest.
            result.Reviews = result.Reviews.OrderByDescending(x => x.Rating.HasValue).ThenByDescending(x => x.CreatedUtc).Take(5).ToList();
            await _store.SetAsync(result.ItemId.ToString("N"), result, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Laurels/TMDb: reviews lookup failed for {Name}", item.Name);
            return cached ?? result;
        }
    }

    internal static string MakeExcerpt(string content)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(content, @"\s+", " ").Trim();
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[*_>#`]+", string.Empty);
        if (text.Length <= ExcerptLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', ExcerptLength);
        return text[..(cut > ExcerptLength / 2 ? cut : ExcerptLength)].TrimEnd('.', ',', ';', ':') + "…";
    }
}
