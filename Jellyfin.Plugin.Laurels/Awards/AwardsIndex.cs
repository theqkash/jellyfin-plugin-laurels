using Jellyfin.Plugin.Laurels.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Awards;

/// <summary>
/// On-disk index of awards per library item, plus the query helpers the sections use.
/// </summary>
public sealed class AwardsIndex
{
    private readonly JsonFileStore<TitleAwards> _store;

    public AwardsIndex(ILogger<AwardsIndex> logger)
    {
        _store = new JsonFileStore<TitleAwards>(Path.Combine(Plugin.Instance?.CacheFolderPath ?? Path.GetTempPath(), "awards"), logger);
    }

    public int Count => _store.Count;

    public IEnumerable<TitleAwards> All => _store.Values;

    public TitleAwards? Get(Guid itemId) => _store.Get(itemId.ToString("N"));

    public Task SetAsync(TitleAwards value, CancellationToken cancellationToken) => _store.SetAsync(value.ItemId.ToString("N"), value, cancellationToken);

    public void Clear() => _store.Clear();

    public IEnumerable<TitleAwards> Winners(IReadOnlyCollection<string> families) =>
        All.Where(t => t.Awards.Any(a => a.Won && families.Contains(a.Family, StringComparer.OrdinalIgnoreCase)));

    public IEnumerable<TitleAwards> Nominees(IReadOnlyCollection<string> families) =>
        All.Where(t => t.Awards.Any(a => families.Contains(a.Family, StringComparer.OrdinalIgnoreCase)));
}
