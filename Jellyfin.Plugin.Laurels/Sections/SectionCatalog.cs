using Jellyfin.Plugin.Laurels.Awards;
using Jellyfin.Plugin.Laurels.Model;

namespace Jellyfin.Plugin.Laurels.Sections;

/// <summary>
/// The home screen sections Laurels can provide. Keys are what the settings page stores;
/// "winners:oscars" / "nominees:cannes" / "top:filmweb" forms are also accepted.
/// </summary>
public sealed record SectionDefinition(string Key, string DisplayText, string Kind, string Argument);

public static class SectionCatalog
{
    public static readonly IReadOnlyList<SectionDefinition> BuiltIn =
    [
        new("award_winners", "Award winners", "winners", "*"),
        new("award_nominees", "Award nominees", "nominees", "*"),
        new("top_filmweb", "Highest rated on Filmweb", "top", RatingSources.Filmweb),
        new("top_imdb", "Highest rated on IMDb", "top", RatingSources.Imdb),
        new("top_metacritic", "Critics' favourites (Metacritic)", "top", RatingSources.Metacritic),
        new("top_tomatoes", "Certified by Rotten Tomatoes", "top", RatingSources.Tomatoes),
    ];

    public static SectionDefinition? Resolve(string key)
    {
        var known = BuiltIn.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        if (known is not null)
        {
            return known;
        }

        var parts = key.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        var kind = parts[0].ToLowerInvariant();
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

                return new SectionDefinition(key, kind == "winners" ? family.Name + " winners" : family.Name + " nominees", kind, family.Key);
            case "top":
                var source = RatingSources.Get(arg);
                return source is null ? null : new SectionDefinition(key, "Highest rated on " + source.DisplayName, "top", source.Key);
            default:
                return null;
        }
    }

    /// <summary>Stable id for the section; Home Screen Sections keys registrations by it.</summary>
    public static string SectionId(string key) => "laurels-" + key.ToLowerInvariant().Replace(':', '-').Replace('_', '-');
}
