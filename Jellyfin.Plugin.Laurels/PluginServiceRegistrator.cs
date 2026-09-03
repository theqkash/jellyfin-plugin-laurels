using Jellyfin.Plugin.Laurels.Awards;
using Jellyfin.Plugin.Laurels.Filmweb;
using Jellyfin.Plugin.Laurels.Ratings;
using Jellyfin.Plugin.Laurels.Reviews;
using Jellyfin.Plugin.Laurels.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Laurels;

/// <summary>
/// Registers plugin services with Jellyfin's dependency container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<MdbListClient>();
        serviceCollection.AddSingleton<FilmwebClient>();
        serviceCollection.AddSingleton<RatingsCache>();
        serviceCollection.AddSingleton<RatingsService>();
        serviceCollection.AddSingleton<WikidataAwardsClient>();
        serviceCollection.AddSingleton<AwardsIndex>();
        serviceCollection.AddSingleton<AwardsService>();
        serviceCollection.AddSingleton<TmdbReviewsClient>();
        serviceCollection.AddHostedService<WebTransformHost>();
    }
}
