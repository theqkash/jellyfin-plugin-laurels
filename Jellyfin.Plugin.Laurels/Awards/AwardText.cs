using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Laurels.Awards;

/// <summary>
/// User-facing strings for sections, collections and captions, in Polish and English.
/// The server language follows the plugin setting, falling back to the server's UI culture; it names the awards
/// library and the home sections. Text sent to a browser follows the viewer's own interface language when the
/// client passes it (see <see cref="UseViewerLanguage"/>).
/// </summary>
public static partial class AwardText
{
    private static readonly AsyncLocal<string?> ViewerLanguage = new();

    private static string _serverLanguage = "en";

    /// <summary>Gets or sets the language in effect: the viewer's for the current request, else the server's. Setting it changes the server language.</summary>
    public static string Language
    {
        get => ViewerLanguage.Value ?? _serverLanguage;
        set => _serverLanguage = value;
    }

    /// <summary>
    /// Uses the viewer's interface language (for example "en-us" or "pl") until the returned scope is disposed.
    /// Languages without translations fall back to the server language.
    /// </summary>
    public static IDisposable UseViewerLanguage(string? language)
    {
        var previous = ViewerLanguage.Value;
        var l = (language ?? string.Empty).Trim().ToLowerInvariant();
        ViewerLanguage.Value = l.StartsWith("pl", StringComparison.Ordinal) ? "pl" : l.StartsWith("en", StringComparison.Ordinal) ? "en" : null;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        public void Dispose() => ViewerLanguage.Value = previous;
    }

    private static bool Pl => Language.StartsWith("pl", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the display name of an award family in the current language.</summary>
    public static string FamilyName(AwardFamily f) => Pl ? f.Key switch
    {
        "oscars" => "Oscary",
        "golden_globes" => "Złote Globy",
        "bafta" => "BAFTA",
        "cannes" => "Cannes",
        "berlin" => "Berlinale",
        "venice" => "Wenecja",
        "emmys" => "Emmy",
        "cesar" => "Cezary",
        "goya" => "Goya",
        "european_film_awards" => "Europejskie Nagrody Filmowe",
        "polish_film_awards" => "Orły",
        "sundance" => "Sundance",
        "annie" => "Annie",
        "saturn" => "Saturn",
        "razzies" => "Złote Maliny",
        "critics_choice" => "Critics' Choice",
        "sag" => "Nagrody Gildii Aktorów (SAG)",
        "independent_spirit" => "Independent Spirit",
        "toronto" => "Toronto (TIFF)",
        "gdynia" => "Gdynia",
        "david" => "David di Donatello",
        "german_film_award" => "Niemiecka Nagroda Filmowa (Lola)",
        "japan_academy" => "Japońska Nagroda Akademii",
        "sitges" => "Sitges",
        "locarno" => "Locarno",
        _ => f.Name,
    } : f.Name;

    /// <summary>Gets the collection / section title for winners of a family.</summary>
    public static string WinnersTitle(AwardFamily f) => Pl ? f.Key switch
    {
        "oscars" => "Zdobywcy Oscarów",
        "golden_globes" => "Zdobywcy Złotych Globów",
        "bafta" => "Zdobywcy nagród BAFTA",
        "cannes" => "Laureaci festiwalu w Cannes",
        "berlin" => "Laureaci Berlinale",
        "venice" => "Laureaci festiwalu w Wenecji",
        "emmys" => "Zdobywcy nagród Emmy",
        "cesar" => "Zdobywcy Cezarów",
        "goya" => "Zdobywcy nagród Goya",
        "european_film_awards" => "Laureaci Europejskich Nagród Filmowych",
        "polish_film_awards" => "Zdobywcy Orłów",
        "sundance" => "Laureaci Sundance",
        "annie" => "Zdobywcy nagród Annie",
        "saturn" => "Zdobywcy nagród Saturn",
        "razzies" => "Zdobywcy Złotych Malin",
        "critics_choice" => "Zdobywcy Critics' Choice",
        "sag" => "Zdobywcy nagród Gildii Aktorów",
        "independent_spirit" => "Zdobywcy Independent Spirit",
        "toronto" => "Nagroda Publiczności w Toronto",
        "gdynia" => "Laureaci festiwalu w Gdyni",
        "david" => "Zdobywcy nagród David di Donatello",
        "german_film_award" => "Zdobywcy Niemieckiej Nagrody Filmowej",
        "japan_academy" => "Zdobywcy Japońskiej Nagrody Akademii",
        "sitges" => "Laureaci festiwalu w Sitges",
        "locarno" => "Laureaci festiwalu w Locarno",
        _ => "Laureaci: " + FamilyName(f),
    } : f.IsFestival ? f.Name + " winners" : f.Name + " winners";

    /// <summary>Gets the collection / section title for nominees of a family.</summary>
    public static string NomineesTitle(AwardFamily f) => Pl ? f.Key switch
    {
        "oscars" => "Nominowani do Oscara",
        "golden_globes" => "Nominowani do Złotych Globów",
        "bafta" => "Nominowani do BAFTA",
        "cannes" => "Filmy z konkursu w Cannes",
        "berlin" => "Filmy z konkursu Berlinale",
        "venice" => "Filmy z konkursu w Wenecji",
        "emmys" => "Nominowani do Emmy",
        "cesar" => "Nominowani do Cezara",
        "goya" => "Nominowani do nagród Goya",
        "european_film_awards" => "Nominowani do Europejskich Nagród Filmowych",
        "polish_film_awards" => "Nominowani do Orłów",
        "razzies" => "Nominowani do Złotych Malin",
        "critics_choice" => "Nominowani do Critics' Choice",
        "sag" => "Nominowani do nagród Gildii Aktorów",
        "independent_spirit" => "Nominowani do Independent Spirit",
        "gdynia" => "Filmy z konkursu w Gdyni",
        "david" => "Nominowani do David di Donatello",
        "german_film_award" => "Nominowani do Niemieckiej Nagrody Filmowej",
        "japan_academy" => "Nominowani do Japońskiej Nagrody Akademii",
        _ => "Nominowani: " + FamilyName(f),
    } : f.Name + " nominees";

    public static string TopTitle(string sourceName) => Pl ? "Najwyżej oceniane: " + sourceName : "Highest rated on " + sourceName;

    public static string AllWinnersTitle => Pl ? "Nagrodzone filmy" : "Award winners";

    public static string AllNomineesTitle => Pl ? "Nominowane filmy" : "Award nominees";

    public static string DefaultLibraryName => Pl ? "Nagrody" : "Awards";

    public static string Winner => Pl ? "zwycięzca" : "winner";

    public static string Nominee => Pl ? "nominacja" : "nominee";

    public static string Wins(int n) => Pl ? (n == 1 ? "1 nagroda" : n < 5 ? n + " nagrody" : n + " nagród") : (n == 1 ? "1 win" : n + " wins");

    public static string Nominations(int n) => Pl ? (n == 1 ? "1 nominacja" : n < 5 ? n + " nominacje" : n + " nominacji") : (n == 1 ? "1 nomination" : n + " nominations");

    /// <summary>
    /// Shortens a Wikidata category label to the part after the award name:
    /// "Academy Award for Best Picture" → "Best Picture", "Oscar dla najlepszego filmu" → "najlepszy film" stays as is
    /// (Polish labels are already in a readable case), "Złoty Glob za najlepszą piosenkę" → "najlepsza piosenka" is not
    /// attempted; we only cut the prefix.
    /// </summary>
    public static string ShortCategory(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        // Wikidata items without an English label come through as bare ids such as "Q106150094": not readable
        if (QidRegex().IsMatch(label.Trim()))
        {
            return string.Empty;
        }

        var m = PrefixRegex().Match(label);
        var rest = m.Success ? label[(m.Index + m.Length)..].Trim() : label.Trim();
        rest = FestivalPrefix().Replace(rest, string.Empty);
        rest = FestivalSuffix().Replace(rest, string.Empty).Trim();
        if (rest.Length == 0)
        {
            return label;
        }

        // the award itself ("Annie Award", "Saturn Awards", "73rd Primetime Creative Arts Emmy Awards") names no category
        if (GenericAward().IsMatch(rest))
        {
            return string.Empty;
        }

        return CategoryNames.Localize(char.ToUpper(rest[0], CultureInfo.InvariantCulture) + rest[1..]);
    }

    [GeneratedRegex(@"^Q\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex QidRegex();

    // "Sitges Film Festival Best Director award", "Cannes Best Director Award": the festival name leads instead of "for"
    [GeneratedRegex(@"^(?:Sitges Film Festival|Sundance Film Festival|Cannes|Berlinale|Venice)\s+(?=\S)|(?<=\S)\s+award$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FestivalPrefix();

    [GeneratedRegex(@"\s+(?:of the Festival de Cannes|at (?:the )?Berlinale|at the [\w ]+ Film Festival)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FestivalSuffix();

    [GeneratedRegex(@"^(?:\d+(?:st|nd|rd|th)\s+)?(?:Primetime\s+)?(?:Creative Arts\s+)?(?:Primetime Emmy|Emmy|Annie|Saturn|Actor|Academy|David di Donatello|Goya|César|BAFTA|Golden Globe)(?:\s+Awards?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GenericAward();

    /// <summary>Orders categories so the headline ones (best picture, director, acting) come first.</summary>
    public static int CategoryRank(string shortCategory)
    {
        if (RankTop().IsMatch(shortCategory)) { return 0; }
        if (RankDirector().IsMatch(shortCategory)) { return 1; }
        if (RankActing().IsMatch(shortCategory)) { return 2; }
        if (RankWriting().IsMatch(shortCategory)) { return 3; }
        if (RankFeature().IsMatch(shortCategory)) { return 4; }
        return 5;
    }

    [GeneratedRegex(@"^Najlepszy film$|^Najlepszy film \(|^Najlepszy serial|^Best (Motion )?Picture|^Best Film$|^Picture$|^Film$|Złota Palma|Palme d'Or|Złoty Niedźwiedź|Golden Bear|Złoty Lew|Golden Lion|Grand Prix|Wielka Nagroda|Grand Jury|Outstanding .*Series$|Najlepszy film europejski|European Film$|Najgorszy film|Worst Picture", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RankTop();

    [GeneratedRegex(@"reżyseri|Direct", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RankDirector();

    [GeneratedRegex(@"aktor|Actor|Actress|Performance|Volpi|rolę", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RankActing();

    [GeneratedRegex(@"scenariusz|Screenplay|Writing|Screenwriter", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RankWriting();

    [GeneratedRegex(@"międzynarodow|International|Foreign|animowany|Animated|dokumentaln|Documentary", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RankFeature();

    // Award name followed by "for"/"dla"/"za" (or a dash/colon), as used by Wikidata labels in en and pl.
    [GeneratedRegex(@"^(?:.*?(?:Award|Awards|Prize|Oscar|Oscara|Globe|Glob|BAFTA|César|Cezar|Goya|Emmy|Orzeł|Orły|Saturn|Annie|Raspberry|Malina|Nagroda|Bear|Lion|Palme|Niedźwiedź|Lew|Palma|Donatello)[^:–-]*?)\s+(?:for|dla|za)\s+|^(?:[^:–-]{3,60})(?:\s[:–-]\s)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrefixRegex();
}
