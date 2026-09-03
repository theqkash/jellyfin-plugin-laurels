using Jellyfin.Plugin.Laurels.Model;
using Jellyfin.Plugin.Laurels.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Ratings;

/// <summary>
/// On-disk cache of everything fetched per library item.
/// </summary>
public sealed class RatingsCache
{
    private readonly JsonFileStore<TitleRatings> _store;

    public RatingsCache(ILogger<RatingsCache> logger)
    {
        var folder = Path.Combine(Plugin.Instance?.CacheFolderPath ?? Path.GetTempPath(), "ratings");
        _store = new JsonFileStore<TitleRatings>(folder, logger);
    }

    public int Count => _store.Count;

    public IEnumerable<TitleRatings> All => _store.Values;

    public TitleRatings? Get(Guid itemId) => _store.Get(itemId.ToString("N"));

    public Task SetAsync(TitleRatings value, CancellationToken cancellationToken) => _store.SetAsync(value.ItemId.ToString("N"), value, cancellationToken);

    public void Remove(Guid itemId) => _store.Remove(itemId.ToString("N"));

    public void Clear() => _store.Clear();
}
