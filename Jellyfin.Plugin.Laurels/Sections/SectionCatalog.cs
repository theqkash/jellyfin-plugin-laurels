using Jellyfin.Plugin.Laurels.Awards;
using Jellyfin.Plugin.Laurels.Model;

namespace Jellyfin.Plugin.Laurels.Sections;

/// <summary>
/// The home screen sections Laurels can provide. Keys: "winners:oscars", "nominees:cannes", "top:filmweb",
/// plus the aggregate "award_winners" / "award_nominees".
/// </summary>
public sealed record SectionDefinition(string Key, string DisplayText, string Kind, string Argument);

public static class SectionCatalog
{
    private static readonly string[] LegacyDefaults = ["award_winners", "award_nominees", "top_filmweb", "top_imdb"];

    /// <summary>
    /// Gets the configured section keys. Installations that still carry the original four-key default
    /// are upgraded to the current per-family default set.
    /// </summary>
    public static IReadOnlyList<string> ConfiguredKeys()
    {
        var cfg = Plugin.Instance?.Configuration;
        var keys = cfg?.HomeSections ?? [];
        if (keys.Length == 0 || keys.OrderBy(k => k).SequenceEqual(LegacyDefaults.OrderBy(k => k), StringComparer.OrdinalIgnoreCase))
        {
            return new Configuration.PluginConfiguration().HomeSections;
        }

        return keys;
    }

    public static SectionDefinition? Resolve(string key)
    {
        key = (key ?? string.Empty).Trim().ToLowerInvariant();
        switch (key)
        {
            case "award_winners":
                return new SectionDefinition(key, AwardText.AllWinnersTitle, "winners", "*");
            case "award_nominees":
                return new SectionDefinition(key, AwardText.AllNomineesTitle, "nominees", "*");
            case "top_filmweb":
                key = "top:filmweb";
                break;
            case "top_imdb":
                key = "top:imdb";
                break;
            case "top_metacritic":
                key = "top:metacritic";
                break;
            case "top_tomatoes":
                key = "top:tomatoes";
                break;
        }

        var parts = key.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        var kind = parts[0];
        var arg = parts[1];
        switch (kind)
        {
            case "winners":
            case "nominees":
                var family = AwardFamilies.Get(arg);
                if (family is null)
                {
                    return null;
                }

                return new SectionDefinition(key, kind == "winners" ? AwardText.WinnersTitle(family) : AwardText.NomineesTitle(family), kind, family.Key);
            case "top":
                var source = RatingSources.Get(arg);
                return source is null ? null : new SectionDefinition(key, AwardText.TopTitle(source.DisplayName), "top", source.Key);
            default:
                return null;
        }
    }

    /// <summary>Stable id for the section; Home Screen Sections keys registrations by it.</summary>
    public static string SectionId(string key) => "laurels-" + key.ToLowerInvariant().Replace(':', '-').Replace('_', '-');
}
