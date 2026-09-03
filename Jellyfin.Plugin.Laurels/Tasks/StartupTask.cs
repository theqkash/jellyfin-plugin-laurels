using Jellyfin.Plugin.Laurels.Sections;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Tasks;

/// <summary>
/// Runs at server startup (after every plugin is loaded) to register home screen sections.
/// A startup-triggered task is used because plugin constructors run before other plugins exist.
/// </summary>
public sealed class StartupTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ILogger<StartupTask> _logger;
    private readonly Awards.AwardsService _awards;

    public StartupTask(ILogger<StartupTask> logger, Awards.AwardsService awards)
    {
        _logger = logger;
        _awards = awards;
    }

    public static int RegisteredSections { get; private set; }

    public string Name => "Laurels startup";

    public string Key => "LaurelsStartup";

    public string Description => "Registers Laurels home screen sections with the Home Screen Sections plugin.";

    public string Category => "Laurels";

    public bool IsHidden => true;

    public bool IsEnabled => true;

    public bool IsLogged => false;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
    }

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var cfg = Plugin.Instance?.Configuration;
        _awards.ApplyLanguage();
        if (cfg?.EnableHomeSections == true)
        {
            RegisteredSections = HomeSectionsBridge.RegisterAll(Sections.SectionCatalog.ConfiguredKeys(), _logger);
        }

        progress.Report(100);
        return Task.CompletedTask;
    }
}
