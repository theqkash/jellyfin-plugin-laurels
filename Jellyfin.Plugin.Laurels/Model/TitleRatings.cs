using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Laurels.Model;

/// <summary>
/// Everything Laurels knows about one library item, persisted in the on-disk cache.
/// </summary>
public sealed class TitleRatings
{
    [JsonPropertyName("itemId")]
    public Guid ItemId { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "movie";

    [JsonPropertyName("fetchedUtc")]
    public DateTimeOffset FetchedUtc { get; set; }

    [JsonPropertyName("ids")]
    public Dictionary<string, string> Ids { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("ratings")]
    public List<RatingEntry> Ratings { get; set; } = [];

    /// <summary>Gets or sets extra flags such as tomatoes_certified_fresh, metacritic_must_see, audience_verified_hot.</summary>
    [JsonPropertyName("flags")]
    public List<string> Flags { get; set; } = [];

    /// <summary>Gets or sets the source key written into CommunityRating on the last apply.</summary>
    [JsonPropertyName("appliedCommunitySource")]
    public string? AppliedCommunitySource { get; set; }

    /// <summary>Gets or sets the source key written into CriticRating on the last apply.</summary>
    [JsonPropertyName("appliedCriticSource")]
    public string? AppliedCriticSource { get; set; }

    /// <summary>Gets or sets which source keys failed on the last refresh (so they can be retried earlier).</summary>
    [JsonPropertyName("failedSources")]
    public List<string> FailedSources { get; set; } = [];

    public RatingEntry? Find(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        return Ratings.FirstOrDefault(r => string.Equals(r.Source, source, StringComparison.OrdinalIgnoreCase) && r.Score.HasValue);
    }

    public void Upsert(RatingEntry entry)
    {
        var idx = Ratings.FindIndex(r => string.Equals(r.Source, entry.Source, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            Ratings[idx] = entry;
        }
        else
        {
            Ratings.Add(entry);
        }
    }
}
