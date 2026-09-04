using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Laurels.Configuration;
using Jellyfin.Plugin.Laurels.Filmweb;
using Jellyfin.Plugin.Laurels.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Ratings;

public enum RefreshOutcome
{
    Updated,
    Unchanged,
    FreshInCache,
    NoIds,
    NotFound,
    RateLimited,
    InvalidKey,
    Skipped,
}

/// <summary>
/// Fetches ratings for one item from every enabled source, caches them and writes the chosen
/// ones into Jellyfin's CommunityRating / CriticRating fields.
/// </summary>
public sealed class RatingsService
{
    private readonly ILibraryManager _libraryManager;
    private readonly MdbListClient _mdbList;
    private readonly FilmwebClient _filmweb;
    private readonly RatingsCache _cache;
    private readonly ILogger<RatingsService> _logger;

    public RatingsService(ILibraryManager libraryManager, MdbListClient mdbList, FilmwebClient filmweb, RatingsCache cache, ILogger<RatingsService> logger)
    {
        _libraryManager = libraryManager;
        _mdbList = mdbList;
        _filmweb = filmweb;
        _cache = cache;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public static bool IsSupported(BaseItem item) => item is Movie or Series;

    public TitleRatings? GetCached(Guid itemId) => _cache.Get(itemId);

    /// <summary>
    /// Gets every supported library item (movies and series, no virtual placeholders).
    /// </summary>
    public IReadOnlyList<BaseItem> GetLibraryItems()
    {
        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            Recursive = true,
        });
    }

    public async Task<RefreshOutcome> RefreshAsync(BaseItem item, bool force, CancellationToken cancellationToken)
    {
        if (!IsSupported(item))
        {
            return RefreshOutcome.Skipped;
        }

        var cfg = Config;
        var existing = _cache.Get(item.Id);
        var ttl = existing is not null && existing.FailedSources.Count > 0 ? TimeSpan.FromHours(12) : TimeSpan.FromDays(Math.Max(1, cfg.CacheDays));
        if (!force && existing is not null && existing.FetchedUtc + ttl > DateTimeOffset.UtcNow)
        {
            // Data is fresh; still make sure the Jellyfin fields reflect current settings.
            var applied = ApplyToItem(item, existing, cfg);
            if (applied)
            {
                await SaveItemAsync(item, cancellationToken).ConfigureAwait(false);
                return RefreshOutcome.Updated;
            }

            return RefreshOutcome.FreshInCache;
        }

        var isSeries = item is Series;
        var mediaType = isSeries ? "show" : "movie";
        var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
        var imdbId = item.GetProviderId(MetadataProvider.Imdb);
        if (string.IsNullOrWhiteSpace(tmdbId) && string.IsNullOrWhiteSpace(imdbId))
        {
            return RefreshOutcome.NoIds;
        }

        var result = existing ?? new TitleRatings { ItemId = item.Id };
        result.Kind = mediaType;
        result.FailedSources.Clear();
        if (!string.IsNullOrWhiteSpace(tmdbId))
        {
            result.Ids["tmdb"] = tmdbId;
        }

        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            result.Ids["imdb"] = imdbId;
        }

        // 1. MDBList: one call, many ratings. When the daily quota is used up we remember that until
        //    midnight UTC (MDBList resets then), skip MDBList for the remaining titles and still fetch
        //    Filmweb; the titles are marked so the next run picks MDBList up again.
        var mdbLimited = false;
        if (!string.IsNullOrWhiteSpace(cfg.MdbListApiKey))
        {
            if (_mdbListExhaustedUntilUtc > DateTimeOffset.UtcNow)
            {
                mdbLimited = true;
                result.FailedSources.Add("mdblist");
            }
            else
            {
                var (provider, id) = !string.IsNullOrWhiteSpace(tmdbId) ? ("tmdb", tmdbId) : ("imdb", imdbId!);
                var mdb = await _mdbList.GetAsync(provider, mediaType, id, cfg.MdbListApiKey, cancellationToken).ConfigureAwait(false);
                if (mdb.RateLimited)
                {
                    _mdbListExhaustedUntilUtc = DateTimeOffset.UtcNow.Date.AddDays(1);
                    _logger.LogWarning("Laurels: MDBList daily quota used up; continuing with Filmweb only until {Reset:u}.", _mdbListExhaustedUntilUtc);
                    mdbLimited = true;
                    result.FailedSources.Add("mdblist");
                }
                else if (mdb.InvalidKey)
                {
                    return RefreshOutcome.InvalidKey;
                }
                else if (mdb.Success)
            {
                result.Ratings.RemoveAll(r => !IsFilmwebSource(r.Source));
                foreach (var entry in mdb.Ratings)
                {
                    result.Upsert(entry);
                }

                foreach (var (k, v) in mdb.Ids)
                {
                    result.Ids.TryAdd(k, v);
                }

                result.Flags = mdb.Flags;
            }
                else if (mdb.NotFound)
                {
                    // Nothing to retry; MDBList simply does not know the title.
                    result.Ratings.RemoveAll(r => !IsFilmwebSource(r.Source));
                }
                else
                {
                    result.FailedSources.Add("mdblist");
                    _logger.LogDebug("Laurels: MDBList lookup failed for {Name}: {Error}", item.Name, mdb.Error);
                }
            }
        }

        // 2. Filmweb: resolve id once, then read both ratings.
        if (cfg.EnableFilmweb)
        {
            await RefreshFilmwebAsync(item, result, isSeries, cancellationToken).ConfigureAwait(false);
        }

        result.FetchedUtc = DateTimeOffset.UtcNow;
        await _cache.SetAsync(result, cancellationToken).ConfigureAwait(false);

        var changed = ApplyToItem(item, result, cfg);
        if (changed)
        {
            await SaveItemAsync(item, cancellationToken).ConfigureAwait(false);
            return RefreshOutcome.Updated;
        }

        if (mdbLimited)
        {
            return RefreshOutcome.RateLimited;
        }

        return result.Ratings.Count == 0 ? RefreshOutcome.NotFound : RefreshOutcome.Unchanged;
    }

    private static DateTimeOffset _mdbListExhaustedUntilUtc = DateTimeOffset.MinValue;

    /// <summary>Gets when MDBList becomes usable again, or null when it is not rate limited.</summary>
    public static DateTimeOffset? MdbListExhaustedUntilUtc => _mdbListExhaustedUntilUtc > DateTimeOffset.UtcNow ? _mdbListExhaustedUntilUtc : null;

    private async Task RefreshFilmwebAsync(BaseItem item, TitleRatings result, bool isSeries, CancellationToken cancellationToken)
    {
        int filmwebId = 0;
        if (item.TryGetProviderId(FilmwebExternalId.ProviderKey, out var fromItem) && int.TryParse(fromItem, out var parsed))
        {
            filmwebId = parsed;
        }
        else if (result.Ids.TryGetValue("filmweb", out var fromCache) && int.TryParse(fromCache, out var parsedCache))
        {
            filmwebId = parsedCache;
        }
        else
        {
            var match = await _filmweb.FindAsync(isSeries, item.Name, item.OriginalTitle, item.ProductionYear, cancellationToken).ConfigureAwait(false);
            if (match is not null)
            {
                filmwebId = match.Id;
                item.SetProviderId(FilmwebExternalId.ProviderKey, match.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                result.Ids["filmweb"] = item.GetProviderId(FilmwebExternalId.ProviderKey)!;
                _providerIdTouched.Add(item.Id);
            }
        }

        if (filmwebId <= 0)
        {
            result.Ratings.RemoveAll(r => IsFilmwebSource(r.Source));
            return;
        }

        result.Ids["filmweb"] = filmwebId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ratings = await _filmweb.GetRatingsAsync(filmwebId, cancellationToken).ConfigureAwait(false);
        if (ratings is null)
        {
            result.FailedSources.Add(RatingSources.Filmweb);
            return;
        }

        var url = FilmwebClient.UrlFor(filmwebId);
        if (ratings.UserRating.HasValue)
        {
            result.Upsert(new RatingEntry
            {
                Source = RatingSources.Filmweb,
                Value = Math.Round(ratings.UserRating.Value, 1),
                Score = RatingSources.ToScore(RatingSources.Filmweb, ratings.UserRating),
                Votes = ratings.UserVotes,
                Url = url,
            });
        }

        if (ratings.CriticRating.HasValue)
        {
            result.Upsert(new RatingEntry
            {
                Source = RatingSources.FilmwebCritics,
                Value = Math.Round(ratings.CriticRating.Value, 1),
                Score = RatingSources.ToScore(RatingSources.FilmwebCritics, ratings.CriticRating),
                Votes = ratings.CriticVotes,
                Url = url,
            });
        }
    }

    private readonly HashSet<Guid> _providerIdTouched = [];

    private static bool IsFilmwebSource(string source) =>
        source is RatingSources.Filmweb or RatingSources.FilmwebCritics;

    /// <summary>
    /// Writes the configured sources into the item's rating fields. Returns true when anything changed.
    /// </summary>
    public bool ApplyToItem(BaseItem item, TitleRatings ratings, PluginConfiguration cfg)
    {
        var isSeries = item is Series;
        var communityPrimary = isSeries ? cfg.SeriesCommunitySource : cfg.MovieCommunitySource;
        var communityFallback = isSeries ? cfg.SeriesCommunityFallbackSource : cfg.MovieCommunityFallbackSource;
        var criticPrimary = isSeries ? cfg.SeriesCriticSource : cfg.MovieCriticSource;
        var criticFallback = isSeries ? cfg.SeriesCriticFallbackSource : cfg.MovieCriticFallbackSource;

        var changed = _providerIdTouched.Remove(item.Id);

        var community = Pick(ratings, communityPrimary, communityFallback, out var communitySource);
        if (community is not null && (cfg.OverwriteExistingRatings || item.CommunityRating is null or 0))
        {
            var value = community.ValueOutOf10;
            if (value.HasValue && (item.CommunityRating is null || Math.Abs(item.CommunityRating.Value - value.Value) > 0.001f))
            {
                item.CommunityRating = value;
                changed = true;
            }
        }

        var critic = Pick(ratings, criticPrimary, criticFallback, out var criticSource);
        if (critic is not null && (cfg.OverwriteExistingRatings || item.CriticRating is null or 0))
        {
            var value = critic.Score;
            if (value.HasValue && (item.CriticRating is null || Math.Abs(item.CriticRating.Value - value.Value) > 0.001f))
            {
                item.CriticRating = value;
                changed = true;
            }
        }

        if (ratings.AppliedCommunitySource != communitySource || ratings.AppliedCriticSource != criticSource)
        {
            ratings.AppliedCommunitySource = communitySource;
            ratings.AppliedCriticSource = criticSource;
        }

        return changed;
    }

    private static RatingEntry? Pick(TitleRatings ratings, string? primary, string? fallback, out string? used)
    {
        var p = RatingSources.Normalize(primary);
        var f = RatingSources.Normalize(fallback);
        used = null;
        if (p.Length > 0 && p != "none")
        {
            var e = ratings.Find(p);
            if (e is not null)
            {
                used = p;
                return e;
            }
        }

        if (f.Length > 0 && f != "none")
        {
            var e = ratings.Find(f);
            if (e is not null)
            {
                used = f;
                return e;
            }
        }

        return null;
    }

    private async Task SaveItemAsync(BaseItem item, CancellationToken cancellationToken)
    {
        await _libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
    }
}
