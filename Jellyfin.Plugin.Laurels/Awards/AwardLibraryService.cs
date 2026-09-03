using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Awards;

/// <summary>
/// Maintains a dedicated "Awards" library (a Jellyfin library of type "collections") that holds one
/// collection per award family: "Oscar winners", "Oscar nominees", "Cannes winners"... The collections
/// are ordinary BoxSets, so they work in every client, and their membership is synced from the awards
/// index after each refresh. Nothing else in the user's library is touched.
/// </summary>
public sealed class AwardLibraryService
{
    private const string ProviderKey = "LaurelsCollection";
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly IFileSystem _fileSystem;
    private readonly AwardsIndex _index;
    private readonly ILogger<AwardLibraryService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public AwardLibraryService(ILibraryManager libraryManager, ICollectionManager collectionManager, IFileSystem fileSystem, AwardsIndex index, ILogger<AwardLibraryService> logger)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _fileSystem = fileSystem;
        _index = index;
        _logger = logger;
    }

    public sealed record ManagedCollection(string Key, string Kind, string Family, string Title);

    public sealed record CollectionState(string Key, string Title, Guid? CollectionId, int ItemCount);

    /// <summary>
    /// Physical path that backs the awards library. The last segment must be "collections": Jellyfin skips
    /// empty library folders during validation unless they carry that name (the same exemption its own
    /// collections folder relies on), and ours is empty until the first collection is created.
    /// </summary>
    public static string LibraryPath => Path.Combine(Plugin.Instance?.CacheFolderPath ?? Path.GetTempPath(), "collections");

    public static string LibraryName
    {
        get
        {
            var configured = Plugin.Instance?.Configuration.AwardLibraryName;
            return string.IsNullOrWhiteSpace(configured) ? AwardText.DefaultLibraryName : configured.Trim();
        }
    }

    /// <summary>Gets the collections that should exist according to the settings.</summary>
    public static IReadOnlyList<ManagedCollection> DesiredCollections()
    {
        var cfg = Plugin.Instance?.Configuration;
        var families = AwardFamilies.FromCsv(cfg?.AwardFamilies);
        var keys = (cfg?.AwardCollections ?? string.Empty).Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (keys.Count == 0)
        {
            keys = families.Select(f => "winners:" + f.Key).ToList();
            if (families.Any(f => f.Key == "oscars"))
            {
                keys.Insert(1, "nominees:oscars");
            }
        }

        var list = new List<ManagedCollection>();
        foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var parts = key.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0] is not ("winners" or "nominees"))
            {
                continue;
            }

            var family = AwardFamilies.Get(parts[1]);
            if (family is null)
            {
                continue;
            }

            var title = parts[0] == "winners" ? AwardText.WinnersTitle(family) : AwardText.NomineesTitle(family);
            list.Add(new ManagedCollection(key.ToLowerInvariant(), parts[0], family.Key, title));
        }

        return list;
    }

    /// <summary>Gets the BoxSet backing a managed collection key, if it exists.</summary>
    public BoxSet? FindCollection(string key) => ManagedBoxSets().FirstOrDefault(b => string.Equals(b.GetProviderId(ProviderKey), key, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<CollectionState> GetState()
    {
        var existing = ManagedBoxSets();
        return DesiredCollections().Select(d =>
        {
            var box = existing.FirstOrDefault(b => string.Equals(b.GetProviderId(ProviderKey), d.Key, StringComparison.OrdinalIgnoreCase));
            return new CollectionState(d.Key, d.Title, box?.Id, box?.LinkedChildren.Length ?? 0);
        }).ToList();
    }

    /// <summary>
    /// Creates the library and collections when missing and syncs their members with the awards index.
    /// </summary>
    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.EnableAwardLibrary != true)
        {
            return;
        }

        if (!await _syncLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Laurels/Library: sync already running.");
            return;
        }

        try
        {
            var folder = await EnsureLibraryFolderAsync(cancellationToken).ConfigureAwait(false);
            if (folder is null)
            {
                _logger.LogWarning("Laurels/Library: could not create or find the awards library; collections not synced.");
                return;
            }

            var desired = DesiredCollections();
            var existing = ManagedBoxSets();
            var enabledFamilies = AwardFamilies.FromCsv(Plugin.Instance?.Configuration.AwardFamilies).Select(f => f.Key).ToList();

            foreach (var managed in desired)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var wanted = (managed.Kind == "winners" ? _index.Winners([managed.Family]) : _index.Nominees([managed.Family]))
                    .Select(t => t.ItemId)
                    .Where(id => _libraryManager.GetItemById(id) is Movie or MediaBrowser.Controller.Entities.TV.Series)
                    .ToHashSet();

                var box = existing.FirstOrDefault(b => string.Equals(b.GetProviderId(ProviderKey), managed.Key, StringComparison.OrdinalIgnoreCase));
                if (box is null)
                {
                    if (wanted.Count == 0)
                    {
                        continue;
                    }

                    box = await CreateBoxSetAsync(folder, managed, cancellationToken).ConfigureAwait(false);
                    if (box is null)
                    {
                        continue;
                    }
                }
                else if (!string.Equals(box.Name, managed.Title, StringComparison.Ordinal))
                {
                    box.Name = managed.Title;
                    await _libraryManager.UpdateItemAsync(box, folder, ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                }

                var current = box.LinkedChildren.Where(c => c.ItemId.HasValue).Select(c => c.ItemId!.Value).ToHashSet();
                var toAdd = wanted.Except(current).ToList();
                var toRemove = current.Except(wanted).ToList();
                if (toAdd.Count > 0)
                {
                    await _collectionManager.AddToCollectionAsync(box.Id, toAdd).ConfigureAwait(false);
                }

                if (toRemove.Count > 0)
                {
                    await _collectionManager.RemoveFromCollectionAsync(box.Id, toRemove).ConfigureAwait(false);
                }

                if (toAdd.Count > 0 || toRemove.Count > 0)
                {
                    _logger.LogInformation("Laurels/Library: '{Title}' now has {Count} titles (+{Added} / -{Removed})", managed.Title, wanted.Count, toAdd.Count, toRemove.Count);
                }
            }

            // Collections that are no longer configured go away (only ours; identified by provider id).
            foreach (var stale in existing.Where(b => !desired.Any(d => string.Equals(d.Key, b.GetProviderId(ProviderKey), StringComparison.OrdinalIgnoreCase))))
            {
                _logger.LogInformation("Laurels/Library: removing collection '{Name}' (no longer configured)", stale.Name);
                _libraryManager.DeleteItem(stale, new DeleteOptions { DeleteFileLocation = true }, true);
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<Folder?> EnsureLibraryFolderAsync(CancellationToken cancellationToken)
    {
        var path = LibraryPath;
        Directory.CreateDirectory(path);
        var folder = FindLibraryFolder(path);
        if (folder is not null)
        {
            return folder;
        }

        _logger.LogInformation("Laurels/Library: creating library '{Name}' at {Path}", LibraryName, path);
        var options = new LibraryOptions
        {
            PathInfos = [new MediaPathInfo(path)],
            EnableRealtimeMonitor = false,
            SaveLocalMetadata = true,
        };
        await _libraryManager.AddVirtualFolder(LibraryName, CollectionTypeOptions.boxsets, options, refreshLibrary: false).ConfigureAwait(false);

        // The physical folder item appears once the top-level folders are validated; wait briefly for it.
        for (var i = 0; i < 20; i++)
        {
            folder = FindLibraryFolder(path);
            if (folder is not null)
            {
                return folder;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        // Fall back to a root validation (cheap: only top-level folders are touched).
        await _libraryManager.GetUserRootFolder().ValidateChildren(new Progress<double>(), new MediaBrowser.Controller.Providers.MetadataRefreshOptions(new MediaBrowser.Controller.Providers.DirectoryService(_fileSystem)), recursive: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        return FindLibraryFolder(path);
    }

    private Folder? FindLibraryFolder(string path)
    {
        return _libraryManager.RootFolder.Children
            .OfType<Folder>()
            .FirstOrDefault(f => _fileSystem.AreEqual(path, f.Path) || _fileSystem.ContainsSubPath(f.Path, path));
    }

    private async Task<BoxSet?> CreateBoxSetAsync(Folder parent, ManagedCollection managed, CancellationToken cancellationToken)
    {
        // Same layout Jellyfin itself uses for collections: "<Name> [boxset]" folder with collection.xml inside.
        var path = Path.Combine(parent.Path, _fileSystem.GetValidFilename(managed.Title) + " [boxset]");
        try
        {
            var info = Directory.CreateDirectory(path);
            var box = new BoxSet
            {
                Name = managed.Title,
                Path = path,
                IsLocked = true,
                ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderKey] = managed.Key },
                DateCreated = info.CreationTimeUtc,
                DateModified = info.LastWriteTimeUtc,
                DisplayOrder = "PremiereDate",
            };
            parent.AddChild(box);
            await _libraryManager.UpdateItemAsync(box, parent, ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Laurels/Library: created collection '{Title}'", managed.Title);
            return box;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "Laurels/Library: creating collection '{Title}' failed", managed.Title);
            return null;
        }
    }

    private List<BoxSet> ManagedBoxSets()
    {
        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            Recursive = true,
        }).OfType<BoxSet>().Where(b => b.HasProviderId(ProviderKey)).ToList();
    }
}
