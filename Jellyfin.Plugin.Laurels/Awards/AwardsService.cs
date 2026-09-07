using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Awards;

/// <summary>
/// Builds and refreshes the awards index for the library.
/// </summary>
public sealed class AwardsService
{
    private readonly ILibraryManager _libraryManager;
    private readonly WikidataAwardsClient _client;
    private readonly AwardsIndex _index;
    private readonly ILogger<AwardsService> _logger;
    private readonly AwardLibraryService _library;
    private readonly MediaBrowser.Controller.Configuration.IServerConfigurationManager _serverConfig;

    public AwardsService(ILibraryManager libraryManager, WikidataAwardsClient client, AwardsIndex index, AwardLibraryService library, MediaBrowser.Controller.Configuration.IServerConfigurationManager serverConfig, ILogger<AwardsService> logger)
    {
        _libraryManager = libraryManager;
        _client = client;
        _index = index;
        _library = library;
        _serverConfig = serverConfig;
        _logger = logger;
        ApplyLanguage();
    }

    /// <summary>Sets the text language from the plugin setting, falling back to the server UI culture.</summary>
    public void ApplyLanguage()
    {
        var configured = Plugin.Instance?.Configuration.Language;
        var culture = string.IsNullOrWhiteSpace(configured) ? _serverConfig.Configuration.UICulture : configured;
        AwardText.Language = string.IsNullOrWhiteSpace(culture) ? "en" : culture.Trim().ToLowerInvariant();
    }

    public AwardLibraryService Library => _library;

    public AwardsIndex Index => _index;

    public IReadOnlyList<AwardFamily> EnabledFamilies => AwardFamilies.FromCsv(Plugin.Instance?.Configuration.AwardFamilies);

    /// <summary>
    /// Refreshes every movie/series with an IMDb id whose index entry is missing or older than AwardCacheDays.
    /// </summary>
    public async Task RefreshAllAsync(bool force, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ApplyLanguage();
        var families = EnabledFamilies;
        var membership = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in families)
        {
            membership[family.Key] = await _client.GetFamilyMembersAsync(family, cancellationToken).ConfigureAwait(false);
        }

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true,
        });

        var pending = new List<(BaseItem Item, string ImdbId)>();
        foreach (var item in items)
        {
            var imdb = item.GetProviderId(MetadataProvider.Imdb);
            if (string.IsNullOrWhiteSpace(imdb))
            {
                continue;
            }

            var existing = _index.Get(item.Id);
            var ttl = TimeSpan.FromDays(Math.Max(1, Plugin.Instance?.Configuration.AwardCacheDays ?? 180));
            if (!force && existing is not null && existing.FetchedUtc + ttl > DateTimeOffset.UtcNow
                && string.Equals(existing.Language, "en", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            pending.Add((item, imdb.Trim()));
        }

        _logger.LogInformation("Laurels/Awards: {Pending} of {Total} titles need an awards lookup", pending.Count, items.Count);
        var done = 0;
        var consecutiveFailures = 0;
        foreach (var batch in pending.Chunk(WikidataAwardsClient.BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await _client.GetAwardsAsync(batch.Select(b => b.ImdbId).ToList(), "en", cancellationToken).ConfigureAwait(false);
            if (raw is null)
            {
                // Wikidata's public endpoint hiccups now and then; skip this batch (it stays pending for the
                // next run), pause, and give up only after several failures in a row.
                consecutiveFailures++;
                done += batch.Length;
                if (consecutiveFailures >= 5)
                {
                    _logger.LogWarning("Laurels/Awards: Wikidata failed {Count} times in a row, stopping this run; remaining titles are picked up next time.", consecutiveFailures);
                    break;
                }

                _logger.LogWarning("Laurels/Awards: Wikidata query failed, skipping {Count} titles and pausing 30 s.", batch.Length);
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                continue;
            }

            consecutiveFailures = 0;

            var byImdb = raw.GroupBy(r => r.ImdbId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            foreach (var (item, imdbId) in batch)
            {
                var entry = new TitleAwards { ItemId = item.Id, ImdbId = imdbId, FetchedUtc = DateTimeOffset.UtcNow, Language = "en" };
                if (byImdb.TryGetValue(imdbId, out var rows))
                {
                    entry.WikidataQid = rows[0].FilmQid;
                    entry.Awards = Classify(rows, families, membership);
                }

                await _index.SetAsync(entry, cancellationToken).ConfigureAwait(false);
            }

            done += batch.Length;
            progress?.Report(100.0 * done / Math.Max(1, pending.Count));
        }

        try
        {
            await _library.SyncAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Laurels/Library: syncing the awards library failed");
        }

        progress?.Report(100);
    }

    /// <summary>Assigns raw Wikidata statements to award families; drops what belongs to none.</summary>
    internal static List<AwardRecord> Classify(IEnumerable<WikidataAwardsClient.RawAward> rows, IReadOnlyList<AwardFamily> families, IReadOnlyDictionary<string, HashSet<string>> membership)
    {
        var result = new List<AwardRecord>();
        foreach (var row in rows)
        {
            var family = families.FirstOrDefault(f =>
                (membership.TryGetValue(f.Key, out var set) && set.Contains(row.AwardQid))
                || f.LabelPrefixes.Any(p => row.AwardLabel.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
            if (family is null)
            {
                continue;
            }

            // Wikidata often lists both the nomination and the win; keep one record per category, preferring the win.
            var existing = result.FirstOrDefault(r => r.Family == family.Key && r.AwardQid == row.AwardQid && r.Year == row.Year);
            if (existing is not null)
            {
                existing.Won |= row.Won;
                continue;
            }

            result.Add(new AwardRecord { Family = family.Key, Category = row.AwardLabel, AwardQid = row.AwardQid, Won = row.Won, Year = row.Year });
        }

        return result.OrderByDescending(r => r.Won).ThenBy(r => r.Family).ThenBy(r => r.Category).ToList();
    }
}
