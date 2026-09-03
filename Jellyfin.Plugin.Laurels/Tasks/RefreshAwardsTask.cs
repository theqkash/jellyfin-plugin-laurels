using Jellyfin.Plugin.Laurels.Awards;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Laurels.Tasks;

/// <summary>
/// Scheduled task: refreshes the awards index from Wikidata.
/// </summary>
public sealed class RefreshAwardsTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly AwardsService _awards;

    public RefreshAwardsTask(AwardsService awards)
    {
        _awards = awards;
    }

    public string Name => "Refresh Laurels awards";

    public string Key => "LaurelsRefreshAwards";

    public string Description => "Looks up film awards and nominations on Wikidata for movies and series with an IMDb id.";

    public string Category => "Laurels";

    public bool IsHidden => false;

    public bool IsEnabled => true;

    public bool IsLogged => true;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.WeeklyTrigger,
            DayOfWeek = DayOfWeek.Sunday,
            TimeOfDayTicks = TimeSpan.FromHours(4).Ticks,
        };
    }

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.EnableAwards != true)
        {
            progress.Report(100);
            return Task.CompletedTask;
        }

        return _awards.RefreshAllAsync(force: false, progress, cancellationToken);
    }
}
