using System.Globalization;
using System.Net;
using System.Text.Json;
using Jellyfin.Plugin.Laurels.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Ratings;

/// <summary>
/// Client for the MDBList API (https://api.mdblist.com, documented at /docs/).
/// One call returns every rating MDBList aggregates for a title (IMDb, TMDb, Trakt, Letterboxd,
/// Rotten Tomatoes critics and audience, Metacritic critics and users, RogerEbert, MyAnimeList).
/// </summary>
public sealed class MdbListClient
{
    private const string BaseUrl = "https://api.mdblist.com/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MdbListClient> _logger;

    public MdbListClient(IHttpClientFactory httpClientFactory, ILogger<MdbListClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public sealed class Result
    {
        public bool Success { get; init; }

        public bool RateLimited { get; init; }

        public bool InvalidKey { get; init; }

        public bool NotFound { get; init; }

        public int? RateLimitRemaining { get; init; }

        public Dictionary<string, string> Ids { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<RatingEntry> Ratings { get; } = [];

        public List<string> Flags { get; } = [];

        public string? Error { get; init; }
    }

    /// <summary>
    /// Looks a title up by one of its provider ids. Provider is "tmdb" or "imdb"; mediaType "movie" or "show".
    /// </summary>
    public async Task<Result> GetAsync(string provider, string mediaType, string id, string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new Result { Success = false, InvalidKey = true, Error = "missing api key" };
        }

        var url = $"{BaseUrl}{provider}/{mediaType}/{Uri.EscapeDataString(id)}/?apikey={Uri.EscapeDataString(apiKey.Trim())}";
        try
        {
            var http = _httpClientFactory.CreateClient("Laurels.MdbList");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Jellyfin-Laurels/0.1");
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            int? remaining = null;
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) && int.TryParse(values.FirstOrDefault(), out var parsed))
            {
                remaining = parsed;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Laurels/MDBList: daily rate limit reached.");
                return new Result { Success = false, RateLimited = true, RateLimitRemaining = 0 };
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Laurels/MDBList: API key rejected.");
                return new Result { Success = false, InvalidKey = true, Error = "invalid api key" };
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new Result { Success = false, NotFound = true, RateLimitRemaining = remaining };
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Laurels/MDBList: {Status} for {Provider}/{Type}/{Id}", (int)response.StatusCode, provider, mediaType, id);
                return new Result { Success = false, Error = $"http {(int)response.StatusCode}", RateLimitRemaining = remaining };
            }

            return Parse(body, remaining);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Laurels/MDBList: request failed for {Provider}/{Type}/{Id}", provider, mediaType, id);
            return new Result { Success = false, Error = ex.Message };
        }
    }

    private Result Parse(string body, int? remaining)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new Result { Success = false, Error = "unexpected payload" };
        }

        if (root.TryGetProperty("response", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            var error = root.TryGetProperty("error", out var e) ? e.ToString() : "error";
            var notFound = error.Contains("not found", StringComparison.OrdinalIgnoreCase);
            var invalid = error.Contains("api key", StringComparison.OrdinalIgnoreCase);
            var limited = error.Contains("limit", StringComparison.OrdinalIgnoreCase);
            return new Result { Success = false, NotFound = notFound, InvalidKey = invalid, RateLimited = limited, Error = error, RateLimitRemaining = remaining };
        }

        var result = new Result { Success = true, RateLimitRemaining = remaining };

        // Ids live either flat (imdb_id, tmdbid, traktid) or under "ids".
        if (root.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ids.EnumerateObject())
            {
                var v = ValueToString(p.Value);
                if (!string.IsNullOrWhiteSpace(v))
                {
                    result.Ids[p.Name] = v;
                }
            }
        }

        foreach (var (name, key) in new[] { ("imdb_id", "imdb"), ("imdbid", "imdb"), ("tmdbid", "tmdb"), ("traktid", "trakt"), ("tvdbid", "tvdb") })
        {
            if (root.TryGetProperty(name, out var v) && !result.Ids.ContainsKey(key))
            {
                var s = ValueToString(v);
                if (!string.IsNullOrWhiteSpace(s))
                {
                    result.Ids[key] = s;
                }
            }
        }

        if (root.TryGetProperty("ratings", out var ratings) && ratings.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in ratings.EnumerateArray())
            {
                var source = MapSource(r.TryGetProperty("source", out var s) ? s.GetString() : null);
                if (source is null)
                {
                    continue;
                }

                var value = ReadDouble(r, "value");
                var score = ReadDouble(r, "score");
                var votes = ReadDouble(r, "votes");
                if (value is null && score is null)
                {
                    continue;
                }

                var entry = new RatingEntry
                {
                    Source = source,
                    Value = value,
                    Score = score.HasValue ? (int)Math.Round(score.Value) : RatingSources.ToScore(source, value),
                    Votes = votes.HasValue ? (int)votes.Value : null,
                    Url = r.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null,
                };
                result.Ratings.Add(entry);
            }
        }

        var mediaType = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        foreach (var entry in result.Ratings)
        {
            entry.Url = BuildUrl(entry.Source, entry.Url, result.Ids, mediaType);
        }

        if (root.TryGetProperty("certification", out var cert) && cert.ValueKind == JsonValueKind.String)
        {
            result.Ids["certification"] = cert.GetString() ?? string.Empty;
        }

        return result;
    }

    /// <summary>
    /// MDBList returns site-relative paths (or nothing) in "url"; turn them into links a browser can open.
    /// </summary>
    internal static string? BuildUrl(string source, string? raw, IReadOnlyDictionary<string, string> ids, string? mediaType)
    {
        var isShow = string.Equals(mediaType, "show", StringComparison.OrdinalIgnoreCase);
        var path = raw is not null && raw.StartsWith('/') ? raw : raw is not null && !raw.Contains("://", StringComparison.Ordinal) && raw.Length > 0 ? "/" + raw : raw;
        if (path is not null && path.Contains("://", StringComparison.Ordinal))
        {
            return path;
        }

        ids.TryGetValue("imdb", out var imdb);
        ids.TryGetValue("tmdb", out var tmdb);
        ids.TryGetValue("trakt", out var trakt);
        return source switch
        {
            RatingSources.Imdb => imdb is null ? null : "https://www.imdb.com/title/" + imdb + "/",
            RatingSources.Tmdb => tmdb is null ? null : "https://www.themoviedb.org/" + (isShow ? "tv/" : "movie/") + tmdb,
            RatingSources.Trakt => trakt is not null ? "https://trakt.tv/" + (isShow ? "shows/" : "movies/") + trakt
                : tmdb is null ? null : "https://trakt.tv/search/tmdb/" + tmdb + "?id_type=" + (isShow ? "show" : "movie"),
            RatingSources.Tomatoes or RatingSources.Audience => path is null ? null : "https://www.rottentomatoes.com" + path,
            RatingSources.Metacritic or RatingSources.MetacriticUser => path is null ? null : "https://www.metacritic.com/" + (isShow ? "tv" : "movie") + path,
            RatingSources.Letterboxd => path is null ? null : "https://letterboxd.com" + path,
            RatingSources.RogerEbert => path is null ? null : "https://www.rogerebert.com/reviews" + path,
            RatingSources.MyAnimeList => path is null ? null : "https://myanimelist.net" + path,
            _ => null,
        };
    }

    /// <summary>Maps MDBList's source names onto Laurels keys; returns null for sources we do not show.</summary>
    private static string? MapSource(string? source) => source?.ToLowerInvariant() switch
    {
        "imdb" => RatingSources.Imdb,
        "tmdb" => RatingSources.Tmdb,
        "trakt" => RatingSources.Trakt,
        "letterboxd" => RatingSources.Letterboxd,
        "tomatoes" => RatingSources.Tomatoes,
        "popcorn" or "audience" => RatingSources.Audience,
        "metacritic" => RatingSources.Metacritic,
        "metacriticuser" or "metacritic_user" => RatingSources.MetacriticUser,
        "rogerebert" => RatingSources.RogerEbert,
        "myanimelist" or "mal" => RatingSources.MyAnimeList,
        _ => null,
    };

    private static double? ReadDouble(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.String when double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    private static string? ValueToString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetRawText(),
        _ => null,
    };
}
