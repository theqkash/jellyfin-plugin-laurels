using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Laurels.Filmweb;

/// <summary>
/// Registers "Filmweb" as a provider id so it shows up in the metadata editor and can be corrected by hand.
/// </summary>
public sealed class FilmwebExternalId : IExternalId
{
    public const string ProviderKey = "Filmweb";

    public string ProviderName => "Filmweb";

    public string Key => ProviderKey;

    public ExternalIdMediaType? Type => null;

    public bool Supports(IHasProviderIds item) => item is Movie or Series;
}

/// <summary>
/// Adds a "Filmweb" link to the external links of items that have a Filmweb id.
/// </summary>
public sealed class FilmwebExternalUrlProvider : IExternalUrlProvider
{
    public string Name => "Filmweb";

    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item is Movie or Series
            && item.TryGetProviderId(FilmwebExternalId.ProviderKey, out var id)
            && int.TryParse(id, out var numeric))
        {
            yield return FilmwebClient.UrlFor(numeric);
        }
    }
}
