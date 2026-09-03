using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Laurels.Model;

/// <summary>
/// One rating from one service, normalised so every source can be compared.
/// </summary>
public sealed class RatingEntry
{
    /// <summary>Gets or sets the source key (imdb, tmdb, tomatoes, filmweb, ...).</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>Gets or sets the rating on the source's native scale (e.g. 8.2 for IMDb, 74 for Metacritic).</summary>
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    /// <summary>Gets or sets the rating normalised to 0..100.</summary>
    [JsonPropertyName("score")]
    public int? Score { get; set; }

    [JsonPropertyName("votes")]
    public int? Votes { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Gets the rating normalised to 0..10, or null when unknown.</summary>
    [JsonIgnore]
    public float? ValueOutOf10 => Score.HasValue ? (float)Math.Round(Score.Value / 10.0, 1) : null;
}
