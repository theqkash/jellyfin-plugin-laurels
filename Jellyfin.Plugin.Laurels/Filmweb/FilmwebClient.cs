using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Filmweb;

/// <summary>
/// Client for Filmweb's (unofficial, unauthenticated) JSON API. Endpoints verified against
/// www.filmweb.pl/api/v1: live/search, film/{id}/info, film/{id}/rating, film/{id}/critics/rating.
/// Requests are serialised and spaced out so a library refresh never hammers the site.
/// </summary>
public sealed class FilmwebClient
{
    private const string BaseUrl = "https://www.filmweb.pl/api/v1/";
    private static readonly TimeSpan MinSpacing = TimeSpan.FromMilliseconds(700);
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _nextAllowedUtc = DateTimeOffset.MinValue;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FilmwebClient> _logger;

    public FilmwebClient(IHttpClientFactory httpClientFactory, ILogger<FilmwebClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public sealed record FilmwebMatch(int Id, string Title, string? OriginalTitle, int? Year, string Type);

    public sealed record FilmwebRatings(double? UserRating, int? UserVotes, double? CriticRating, int? CriticVotes);

    public static string UrlFor(int id) => $"https://www.filmweb.pl/film?id={id.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Finds the Filmweb entry for a title. A hit is accepted only when the year matches (+-1) and
    /// the title or original title matches after normalisation, so we never attach a wrong rating.
    /// </summary>
    public async Task<FilmwebMatch?> FindAsync(bool isSeries, string? name, string? originalTitle, int? year, CancellationToken cancellationToken)
    {
        var wantedType = isSeries ? "serial" : "film";
        var queries = new List<string>();
        foreach (var candidate in new[] { originalTitle, name })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && !queries.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                queries.Add(candidate.Trim());
            }
        }

        var wantedTitles = queries.Select(NormalizeTitle).Where(t => t.Length > 0).ToList();
        if (wantedTitles.Count == 0)
        {
            return null;
        }

        foreach (var query in queries)
        {
            var hits = await SearchAsync(query, cancellationToken).ConfigureAwait(false);
            var candidates = hits.Where(h => string.Equals(h.Type, wantedType, StringComparison.OrdinalIgnoreCase) && h.Id > 0).Take(6).ToList();
            foreach (var hit in candidates)
            {
                var info = await GetInfoAsync(hit.Id, cancellationToken).ConfigureAwait(false);
                if (info is null)
                {
                    continue;
                }

                var yearOk = !year.HasValue || !info.Year.HasValue || Math.Abs(info.Year.Value - year.Value) <= 1;
                var titles = new[] { info.Title, info.OriginalTitle, hit.MatchedTitle }.Select(NormalizeTitle).Where(t => t.Length > 0);
                var titleOk = titles.Any(t => wantedTitles.Contains(t, StringComparer.Ordinal));
                if (yearOk && titleOk)
                {
                    return new FilmwebMatch(hit.Id, info.Title ?? hit.MatchedTitle ?? query, info.OriginalTitle, info.Year, wantedType);
                }
            }
        }

        _logger.LogDebug("Laurels/Filmweb: no confident match for '{Name}' ({Year})", name, year);
        return null;
    }

    public async Task<FilmwebRatings?> GetRatingsAsync(int filmwebId, CancellationToken cancellationToken)
    {
        var user = await GetJsonAsync<RatingDto>($"film/{filmwebId.ToString(CultureInfo.InvariantCulture)}/rating", cancellationToken).ConfigureAwait(false);
        var critics = await GetJsonAsync<RatingDto>($"film/{filmwebId.ToString(CultureInfo.InvariantCulture)}/critics/rating", cancellationToken).ConfigureAwait(false);
        if (user is null && critics is null)
        {
            return null;
        }

        return new FilmwebRatings(
            user?.Rate > 0 ? Math.Round(user.Rate, 2) : null,
            user?.Count > 0 ? user.Count : null,
            critics?.Rate > 0 ? Math.Round(critics.Rate, 2) : null,
            critics?.Count > 0 ? critics.Count : null);
    }

    private async Task<List<SearchHitDto>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var dto = await GetJsonAsync<SearchResponseDto>("live/search?query=" + Uri.EscapeDataString(query), cancellationToken).ConfigureAwait(false);
        return dto?.SearchHits ?? [];
    }

    private Task<InfoDto?> GetInfoAsync(int id, CancellationToken cancellationToken) =>
        GetJsonAsync<InfoDto>($"film/{id.ToString(CultureInfo.InvariantCulture)}/info", cancellationToken);

    private async Task<T?> GetJsonAsync<T>(string relativeUrl, CancellationToken cancellationToken)
        where T : class
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _nextAllowedUtc - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            var http = _httpClientFactory.CreateClient("Laurels.Filmweb");
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + relativeUrl);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Jellyfin-Laurels/0.1)");
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.AcceptLanguage.ParseAdd("pl-PL,pl;q=0.9,en;q=0.5");
            request.Headers.TryAddWithoutValidation("x-locale", "pl");

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            _nextAllowedUtc = DateTimeOffset.UtcNow.Add(MinSpacing);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _nextAllowedUtc = DateTimeOffset.UtcNow.AddSeconds(10);
                _logger.LogWarning("Laurels/Filmweb: rate limited on {Url}, backing off", relativeUrl);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Laurels/Filmweb: {Status} for {Url}", (int)response.StatusCode, relativeUrl);
                return null;
            }

            // Filmweb answers 200 with an empty body when there is no data (e.g. no critics' rating).
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body) || body == "[]" || body == "{}")
            {
                return null;
            }

            return System.Text.Json.JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Laurels/Filmweb: request failed for {Url}", relativeUrl);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Lower-cases, strips diacritics and punctuation, collapses whitespace.</summary>
    internal static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        // "ł" does not decompose; map it by hand.
        return sb.ToString().Replace('ł', 'l').Trim();
    }

    private sealed class SearchResponseDto
    {
        [JsonPropertyName("searchHits")]
        public List<SearchHitDto> SearchHits { get; set; } = [];
    }

    private sealed class SearchHitDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("matchedTitle")]
        public string? MatchedTitle { get; set; }
    }

    private sealed class InfoDto
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("originalTitle")]
        public string? OriginalTitle { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    private sealed class RatingDto
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("rate")]
        public double Rate { get; set; }
    }
}
