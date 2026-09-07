using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Laurels.Configuration;

/// <summary>
/// Plugin settings. Rating "sources" are identified by lower-case keys, e.g. imdb, tmdb, trakt,
/// letterboxd, tomatoes, audience, metacritic, metacritic_user, rogerebert, filmweb, filmweb_critics.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // --- Data sources -------------------------------------------------------------------------

    /// <summary>Gets or sets the MDBList API key (https://mdblist.com/preferences/).</summary>
    public string MdbListApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether Filmweb lookups are performed.</summary>
    public bool EnableFilmweb { get; set; } = true;

    /// <summary>Gets or sets the TMDb API key used for review excerpts (optional).</summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    // --- What gets written into Jellyfin's own rating fields -----------------------------------

    public string MovieCommunitySource { get; set; } = "imdb";

    public string MovieCommunityFallbackSource { get; set; } = "tmdb";

    public string MovieCriticSource { get; set; } = "metacritic";

    public string MovieCriticFallbackSource { get; set; } = "tomatoes";

    public string SeriesCommunitySource { get; set; } = "imdb";

    public string SeriesCommunityFallbackSource { get; set; } = "tmdb";

    public string SeriesCriticSource { get; set; } = "metacritic";

    public string SeriesCriticFallbackSource { get; set; } = "tomatoes";

    /// <summary>Gets or sets a value indicating whether existing non-empty ratings may be overwritten.</summary>
    public bool OverwriteExistingRatings { get; set; } = true;

    /// <summary>Gets or sets how long fetched ratings stay valid before being refreshed.</summary>
    public int CacheDays { get; set; } = 365;

    /// <summary>Gets or sets how long an awards lookup stays valid before Wikidata is asked again.</summary>
    public int AwardCacheDays { get; set; } = 180;

    /// <summary>Gets or sets the max number of titles processed per scheduled run (0 = unlimited).</summary>
    public int MaxItemsPerRun { get; set; } = 0;

    // --- Web UI (requires the File Transformation plugin) ---------------------------------------

    public bool EnableWebUi { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the star icon next to the community rating is replaced by the source logo.</summary>
    public bool ReplaceRatingIcons { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the "all ratings" strip is shown on the detail page.</summary>
    public bool ShowRatingsPanel { get; set; } = true;

    /// <summary>Gets or sets the comma separated, ordered list of sources shown in the ratings strip. Empty = all.</summary>
    public string RatingsPanelSources { get; set; } = "imdb,tmdb,filmweb,filmweb_critics,tomatoes,audience,metacritic,letterboxd,trakt";

    public bool ShowAwardBadges { get; set; } = true;

    public bool ShowReviewExcerpts { get; set; } = false;

    // --- Awards -------------------------------------------------------------------------------

    public bool EnableAwards { get; set; } = true;

    /// <summary>Gets or sets the comma separated award family keys that are indexed and displayed.</summary>
    public string AwardFamilies { get; set; } = "oscars,golden_globes,bafta,cannes,berlin,venice,emmys,cesar,european_film_awards,polish_film_awards,goya,sundance,annie,saturn,razzies,critics_choice,sag,independent_spirit,toronto,gdynia,david,german_film_award,japan_academy,sitges,locarno";

    /// <summary>Gets or sets the language for award names and categories ("pl", "en"; empty = server UI language).</summary>
    public string Language { get; set; } = string.Empty;

    // --- Awards library: a dedicated "Awards" library holding one collection per award family ------

    public bool EnableAwardLibrary { get; set; } = true;

    /// <summary>Gets or sets the library name; empty = "Awards" / "Nagrody" depending on language.</summary>
    public string AwardLibraryName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the collections kept in the awards library, e.g. "winners:oscars,nominees:oscars,winners:cannes".
    /// Empty = winners of every enabled family plus Oscar nominees.
    /// </summary>
    public string AwardCollections { get; set; } = string.Empty;

    // --- Home screen sections (requires Home Screen Sections plugin) ----------------------------

    public bool EnableHomeSections { get; set; } = true;

    /// <summary>
    /// Gets or sets the home screen sections offered to users (they still pick which ones show up in "Modular Home").
    /// Keys: winners:&lt;family&gt;, nominees:&lt;family&gt;, top:&lt;source&gt;, award_winners, award_nominees.
    /// </summary>
    public string[] HomeSections { get; set; } =
    [
        "winners:oscars",
        "nominees:oscars",
        "winners:golden_globes",
        "winners:bafta",
        "winners:cannes",
        "winners:berlin",
        "winners:venice",
        "winners:european_film_awards",
        "winners:polish_film_awards",
        "winners:gdynia",
        "winners:razzies",
        "winners:sundance",
        "winners:saturn",
        "winners:annie",
        "winners:critics_choice",
        "winners:sag",
        "award_winners",
        "top:filmweb",
        "top:imdb",
        "top:metacritic",
    ];
}
