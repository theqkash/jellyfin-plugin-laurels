using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Awards;

/// <summary>
/// Reads film awards from Wikidata's SPARQL endpoint. Two kinds of query are used:
///  1. per award family, the set of award items that belong to it (cached for the run),
///  2. per batch of IMDb ids, every "award received" (P166) and "nominated for" (P1411) statement.
/// Wikidata asks for a descriptive User-Agent and roughly one request per second; both are honoured.
/// </summary>
public sealed class WikidataAwardsClient
{
    private const string Endpoint = "https://query.wikidata.org/sparql";
    private static readonly TimeSpan MinSpacing = TimeSpan.FromMilliseconds(1100);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _nextAllowedUtc = DateTimeOffset.MinValue;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WikidataAwardsClient> _logger;
    private readonly Dictionary<string, HashSet<string>> _familyMembers = new(StringComparer.OrdinalIgnoreCase);

    public WikidataAwardsClient(IHttpClientFactory httpClientFactory, ILogger<WikidataAwardsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public sealed record RawAward(string ImdbId, string FilmQid, string AwardQid, string AwardLabel, bool Won, int? Year);

    /// <summary>Maximum IMDb ids per SPARQL request; keeps queries well under the 60 s limit.</summary>
    public const int BatchSize = 30;

    /// <summary>
    /// Gets the Wikidata items (award categories) belonging to a family, e.g. every Oscar category.
    /// </summary>
    public async Task<HashSet<string>> GetFamilyMembersAsync(AwardFamily family, CancellationToken cancellationToken)
    {
        if (_familyMembers.TryGetValue(family.Key, out var cached))
        {
            return cached;
        }

        var roots = string.Join(' ', family.RootQids.Select(q => "wd:" + q));
        var query = $$"""
            SELECT DISTINCT ?award WHERE {
              VALUES ?root { {{roots}} }
              { ?award wdt:P31 ?root } UNION { ?award wdt:P279 ?root } UNION { ?award wdt:P361 ?root } UNION { ?award wdt:P1027 ?root }
              UNION { ?award wdt:P31/wdt:P279 ?root } UNION { ?award wdt:P279/wdt:P279 ?root } UNION { ?award wdt:P361/wdt:P361 ?root }
            }
            """;
        var members = new HashSet<string>(StringComparer.Ordinal);
        using var doc = await QueryAsync(query, cancellationToken).ConfigureAwait(false);
        if (doc is not null)
        {
            foreach (var row in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
            {
                var qid = Qid(row, "award");
                if (qid is not null)
                {
                    members.Add(qid);
                }
            }
        }

        _logger.LogInformation("Laurels/Wikidata: {Family} has {Count} award categories", family.Key, members.Count);
        _familyMembers[family.Key] = members;
        return members;
    }

    /// <summary>
    /// Fetches every award statement for the given IMDb ids. Returns null when Wikidata could not be reached.
    /// </summary>
    public async Task<List<RawAward>?> GetAwardsAsync(IReadOnlyCollection<string> imdbIds, CancellationToken cancellationToken)
        => await GetAwardsAsync(imdbIds, "en", cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Fetches every award statement for the given IMDb ids, with category labels in the given language
    /// (English fallback). Returns null when Wikidata could not be reached.
    /// </summary>
    public async Task<List<RawAward>?> GetAwardsAsync(IReadOnlyCollection<string> imdbIds, string language, CancellationToken cancellationToken)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.Split('-')[0].ToLowerInvariant();
        var labelLanguages = lang == "en" ? "en" : lang + ",en";
        if (imdbIds.Count == 0)
        {
            return [];
        }

        var values = string.Join(' ', imdbIds.Select(id => "\"" + id.Replace("\"", string.Empty, StringComparison.Ordinal) + "\""));
        var query = $$"""
            SELECT ?imdb ?film ?award ?awardLabel ?won ?date WHERE {
              VALUES ?imdb { {{values}} }
              ?film wdt:P345 ?imdb .
              { ?film p:P166 ?st . ?st ps:P166 ?award . BIND(true AS ?won) }
              UNION
              { ?film p:P1411 ?st . ?st ps:P1411 ?award . BIND(false AS ?won) }
              OPTIONAL { ?st pq:P585 ?date }
              SERVICE wikibase:label { bd:serviceParam wikibase:language "{{labelLanguages}}". }
            }
            """;
        using var doc = await QueryAsync(query, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        var list = new List<RawAward>();
        foreach (var row in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            var imdb = Literal(row, "imdb");
            var film = Qid(row, "film");
            var award = Qid(row, "award");
            if (imdb is null || film is null || award is null)
            {
                continue;
            }

            var label = Literal(row, "awardLabel") ?? award;
            var won = string.Equals(Literal(row, "won"), "true", StringComparison.OrdinalIgnoreCase);
            int? year = null;
            var date = Literal(row, "date");
            if (date is not null && date.Length >= 4 && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y))
            {
                year = y;
            }

            list.Add(new RawAward(imdb, film, award, label, won, year));
        }

        return list;
    }

    private async Task<JsonDocument?> QueryAsync(string sparql, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var wait = _nextAllowedUtc - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                }

                var http = _httpClientFactory.CreateClient("Laurels.Wikidata");
                http.Timeout = TimeSpan.FromSeconds(90);
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
                request.Headers.UserAgent.ParseAdd("Jellyfin-Laurels/0.1 (https://github.com/; jellyfin plugin)");
                request.Headers.Accept.ParseAdd("application/sparql-results+json");
                request.Content = new StringContent("query=" + Uri.EscapeDataString(sparql), Encoding.UTF8, "application/x-www-form-urlencoded");

                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                _nextAllowedUtc = DateTimeOffset.UtcNow.Add(MinSpacing);

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
                {
                    var delay = TimeSpan.FromSeconds(10 * (attempt + 1));
                    _nextAllowedUtc = DateTimeOffset.UtcNow.Add(delay);
                    _logger.LogWarning("Laurels/Wikidata: {Status}, retrying in {Delay}s", (int)response.StatusCode, delay.TotalSeconds);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Laurels/Wikidata: query failed with {Status}", (int)response.StatusCode);
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return JsonDocument.Parse(body);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Laurels/Wikidata: request error (attempt {Attempt})", attempt + 1);
            }
            finally
            {
                Gate.Release();
            }
        }

        return null;
    }

    private static string? Literal(JsonElement row, string name) =>
        row.TryGetProperty(name, out var el) && el.TryGetProperty("value", out var v) ? v.GetString() : null;

    private static string? Qid(JsonElement row, string name)
    {
        var uri = Literal(row, name);
        if (uri is null)
        {
            return null;
        }

        var idx = uri.LastIndexOf('/');
        return idx >= 0 ? uri[(idx + 1)..] : uri;
    }
}
