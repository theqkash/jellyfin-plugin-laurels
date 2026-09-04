using Jellyfin.Plugin.Laurels.Ratings;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Laurels.Tasks;

/// <summary>
/// Scheduled task: walks the library and refreshes ratings whose cache entry is stale.
/// </summary>
public sealed class RefreshRatingsTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly RatingsService _ratings;
    private readonly ILogger<RefreshRatingsTask> _logger;

    public RefreshRatingsTask(RatingsService ratings, ILogger<RefreshRatingsTask> logger)
    {
        _ratings = ratings;
        _logger = logger;
    }

    public string Name => "Refresh Laurels ratings";

    public string Key => "LaurelsRefreshRatings";

    public string Description => "Fetches ratings from MDBList and Filmweb for movies and series and updates community/critic ratings.";

    public string Category => "Laurels";

    public bool IsHidden => false;

    public bool IsEnabled => true;

    public bool IsLogged => true;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(24).Ticks,
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var items = _ratings.GetLibraryItems();
        var max = Plugin.Instance?.Configuration.MaxItemsPerRun ?? 0;
        var counts = new Dictionary<RefreshOutcome, int>();
        var fetched = 0;
        _logger.LogInformation("Laurels: refreshing ratings for {Count} items", items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[i];
            RefreshOutcome outcome;
            try
            {
                outcome = await _ratings.RefreshAsync(item, force: false, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Laurels: refresh failed for {Name}", item.Name);
                outcome = RefreshOutcome.Skipped;
            }

            counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
            if (outcome is RefreshOutcome.InvalidKey)
            {
                _logger.LogWarning("Laurels: stopping run early ({Outcome}).", outcome);
                break;
            }

            if (outcome is not RefreshOutcome.FreshInCache and not RefreshOutcome.Skipped and not RefreshOutcome.NoIds)
            {
                fetched++;
                if (max > 0 && fetched >= max)
                {
                    _logger.LogInformation("Laurels: reached MaxItemsPerRun ({Max}); the rest will be picked up next run.", max);
                    break;
                }
            }

            progress.Report(100.0 * (i + 1) / items.Count);
        }

        _logger.LogInformation("Laurels: refresh finished. {Summary}", string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}")));
        progress.Report(100);
    }
}
