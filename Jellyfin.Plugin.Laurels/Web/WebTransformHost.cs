using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Web;

/// <summary>
/// After the host starts, hooks our index.html transformation into the File Transformation plugin.
/// </summary>
public sealed class WebTransformHost : IHostedService
{
    private static readonly Guid TransformationId = Guid.Parse("4b7c2d1e-8f3a-4c6b-9d2e-1a5f7e9c3b21");
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebTransformHost> _logger;
    private bool _registered;

    public WebTransformHost(IServiceProvider serviceProvider, ILogger<WebTransformHost> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the web UI hook is live (shown on the settings page).</summary>
    public static bool IsActive { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.EnableWebUi != true)
        {
            _logger.LogInformation("Laurels: web UI disabled in settings.");
            return Task.CompletedTask;
        }

        var handler = typeof(IndexHtmlTransform).GetMethod(nameof(IndexHtmlTransform.TransformAsync), BindingFlags.Public | BindingFlags.Static)!;
        // The pattern is a regex matched against the requested path; an unanchored "index.html" would also
        // match e.g. "user-home-index-html.<hash>.chunk.js" and corrupt it.
        _registered = FileTransformationBridge.Register(TransformationId, @"(^|/)index\.html$", handler, _serviceProvider, _logger);
        IsActive = _registered;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_registered)
        {
            FileTransformationBridge.Unregister(TransformationId, _serviceProvider, _logger);
            IsActive = false;
        }

        return Task.CompletedTask;
    }
}
