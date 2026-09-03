namespace Jellyfin.Plugin.Laurels.Model;

/// <summary>
/// Canonical source keys and their display metadata.
/// </summary>
public static class RatingSources
{
    public const string Imdb = "imdb";
    public const string Tmdb = "tmdb";
    public const string Trakt = "trakt";
    public const string Letterboxd = "letterboxd";
    public const string Tomatoes = "tomatoes";
    public const string Audience = "audience";
    public const string Metacritic = "metacritic";
    public const string MetacriticUser = "metacritic_user";
    public const string RogerEbert = "rogerebert";
    public const string MyAnimeList = "myanimelist";
    public const string Filmweb = "filmweb";
    public const string FilmwebCritics = "filmweb_critics";

    public sealed record SourceInfo(string Key, string DisplayName, string Icon, double NativeMax);

    /// <summary>
    /// Gets the known sources in default display order. Icon is the file name under Web/Assets.
    /// </summary>
    public static readonly IReadOnlyList<SourceInfo> All =
    [
        new(Imdb, "IMDb", "imdb.svg", 10),
        new(Tmdb, "TMDb", "tmdb.svg", 10),
        new(Filmweb, "Filmweb", "filmweb.svg", 10),
        new(FilmwebCritics, "Filmweb critics", "filmweb-critics.svg", 10),
        new(Tomatoes, "Rotten Tomatoes", "tomatoes.svg", 100),
        new(Audience, "RT audience", "audience.svg", 100),
        new(Metacritic, "Metacritic", "metacritic.svg", 100),
        new(MetacriticUser, "Metacritic users", "metacritic-user.svg", 10),
        new(Letterboxd, "Letterboxd", "letterboxd.svg", 5),
        new(Trakt, "Trakt", "trakt.svg", 100),
        new(RogerEbert, "RogerEbert.com", "rogerebert.svg", 4),
        new(MyAnimeList, "MyAnimeList", "myanimelist.svg", 10),
    ];

    public static SourceInfo? Get(string? key) =>
        key is null ? null : All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? key) => (key ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Converts a native-scale value to 0..100.</summary>
    public static int? ToScore(string source, double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var max = Get(source)?.NativeMax ?? 10;
        return (int)Math.Round(value.Value / max * 100.0, MidpointRounding.AwayFromZero);
    }
}
