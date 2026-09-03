using System.Globalization;
using Jellyfin.Plugin.Laurels.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Laurels;

/// <summary>
/// Laurels: ratings from many services and award data for your library.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginId = Guid.Parse("9c1f6c3a-2b7e-4d5f-9a1e-6f0b2c4d8e71");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        CacheFolderPath = Path.Combine(applicationPaths.DataPath, "laurels");
        Directory.CreateDirectory(CacheFolderPath);
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Laurels";

    public override Guid Id => PluginId;

    public override string Description => "Ratings from IMDb, TMDb, Rotten Tomatoes, Metacritic, Letterboxd, Trakt and Filmweb, plus film awards and home screen sections.";

    /// <summary>
    /// Gets the folder used for caches and indexes (under Jellyfin's data path).
    /// </summary>
    public string CacheFolderPath { get; }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
            },
        ];
    }
}
