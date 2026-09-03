namespace Jellyfin.Plugin.Laurels.Awards;

/// <summary>
/// An award "family" is everything handed out by one institution or festival (all Oscar categories, all Cannes prizes...).
/// RootQids are the Wikidata items the individual award categories hang off (instance of / subclass of / part of / conferred by).
/// LabelPrefixes are a second, label-based net for categories that are modelled loosely in Wikidata.
/// </summary>
public sealed record AwardFamily(string Key, string Name, string ShortName, string[] RootQids, string[] LabelPrefixes, bool IsFestival);

public static class AwardFamilies
{
    public static readonly IReadOnlyList<AwardFamily> All =
    [
        new("oscars", "Academy Awards", "Oscar", ["Q19020"], ["Academy Award", "Oscar "], false),
        new("golden_globes", "Golden Globe Awards", "Golden Globe", ["Q1011547"], ["Golden Globe Award", "Złoty Glob"], false),
        new("bafta", "BAFTA Film Awards", "BAFTA", ["Q732997"], ["BAFTA Award", "British Academy Film Award", "Nagroda BAFTA", "BAFTA "], false),
        new("cannes", "Cannes Film Festival", "Cannes", ["Q42369"], ["Palme d'Or", "Cannes Film Festival", "Grand Prix (Cannes", "Caméra d'Or", "Un Certain Regard", "Złota Palma", "Festiwal Filmowy w Cannes"], true),
        new("berlin", "Berlin International Film Festival", "Berlinale", ["Q130871"], ["Golden Bear", "Silver Bear", "Berlin International Film Festival", "Złoty Niedźwiedź", "Srebrny Niedźwiedź"], true),
        new("venice", "Venice Film Festival", "Venice", ["Q49024"], ["Golden Lion", "Silver Lion", "Volpi Cup", "Venice Film Festival", "Złoty Lew", "Srebrny Lew", "Puchar Volpiego"], true),
        new("emmys", "Emmy Awards", "Emmy", ["Q123737", "Q1044427"], ["Primetime Emmy Award", "Emmy Award", "Nagroda Emmy", "Emmy "], false),
        new("cesar", "César Awards", "César", ["Q174389"], ["César Award", "Cezar ", "Nagroda Cezara"], false),
        new("goya", "Goya Awards", "Goya", ["Q212828"], ["Goya Award", "Nagroda Goya", "Goya "], false),
        new("european_film_awards", "European Film Awards", "EFA", ["Q223740"], ["European Film Award", "Europejska Nagroda Filmowa"], false),
        new("polish_film_awards", "Polish Film Awards (Orły)", "Orzeł", ["Q497324"], ["Polish Film Award", "Polish Academy Award", "Polska Nagroda Filmowa", "Orzeł "], false),
        new("sundance", "Sundance Film Festival", "Sundance", ["Q189887"], ["Sundance Film Festival", "Sundance "], true),
        new("annie", "Annie Awards", "Annie", ["Q566905"], ["Annie Award", "Nagroda Annie"], false),
        new("saturn", "Saturn Awards", "Saturn", ["Q105447"], ["Saturn Award", "Nagroda Saturn"], false),
        new("razzies", "Golden Raspberry Awards", "Razzie", ["Q40237"], ["Golden Raspberry Award", "Razzie Award", "Złota Malina"], false),
        new("critics_choice", "Critics' Choice Awards", "Critics' Choice", ["Q7585305"], ["Critics' Choice", "Critics Choice"], false),
        new("sag", "Screen Actors Guild Awards", "SAG", ["Q268200"], ["Screen Actors Guild Award", "Actor Award"], false),
        new("independent_spirit", "Independent Spirit Awards", "Spirit", ["Q311836"], ["Independent Spirit Award"], false),
        new("toronto", "Toronto International Film Festival", "TIFF", ["Q39087364"], ["Toronto International Film Festival People's Choice"], true),
        new("gdynia", "Gdynia Film Festival", "Gdynia", ["Q1463060", "Q28056433"], ["Gdynia Film Festival", "Festiwal Polskich Filmów Fabularnych", "Złote Lwy"], true),
        new("david", "David di Donatello", "David", ["Q31579"], ["David di Donatello"], false),
        new("german_film_award", "German Film Awards (Lola)", "Lola", ["Q708731"], ["German Film Award", "Deutscher Filmpreis"], false),
        new("japan_academy", "Japan Academy Film Prize", "Japan Academy", ["Q194258"], ["Japan Academy Prize", "Japan Academy Film Prize"], false),
        new("sitges", "Sitges Film Festival", "Sitges", ["Q975945"], ["Sitges Film Festival"], true),
        new("locarno", "Locarno Film Festival", "Locarno", ["Q502053"], ["Locarno", "Golden Leopard", "Złoty Lampart"], true),
    ];

    public static AwardFamily? Get(string? key) =>
        key is null ? null : All.FirstOrDefault(f => string.Equals(f.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<AwardFamily> FromCsv(string? csv)
    {
        var keys = (csv ?? string.Empty).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = keys.Select(Get).Where(f => f is not null).Cast<AwardFamily>().Distinct().ToList();
        return list.Count == 0 ? All : list;
    }
}
