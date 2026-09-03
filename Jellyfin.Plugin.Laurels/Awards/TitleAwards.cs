using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Laurels.Awards;

public sealed class AwardRecord
{
    [JsonPropertyName("family")]
    public string Family { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("awardQid")]
    public string AwardQid { get; set; } = string.Empty;

    [JsonPropertyName("won")]
    public bool Won { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }
}

/// <summary>
/// Awards and nominations of one library item, as found on Wikidata.
/// </summary>
public sealed class TitleAwards
{
    [JsonPropertyName("itemId")]
    public Guid ItemId { get; set; }

    [JsonPropertyName("imdbId")]
    public string ImdbId { get; set; } = string.Empty;

    [JsonPropertyName("wikidataQid")]
    public string? WikidataQid { get; set; }

    [JsonPropertyName("fetchedUtc")]
    public DateTimeOffset FetchedUtc { get; set; }

    [JsonPropertyName("awards")]
    public List<AwardRecord> Awards { get; set; } = [];

    [JsonIgnore]
    public bool HasWins => Awards.Any(a => a.Won);

    [JsonIgnore]
    public bool HasNominations => Awards.Count > 0;

    public bool WonIn(string family) => Awards.Any(a => a.Won && string.Equals(a.Family, family, StringComparison.OrdinalIgnoreCase));

    public bool NominatedIn(string family) => Awards.Any(a => string.Equals(a.Family, family, StringComparison.OrdinalIgnoreCase));
}
