using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Laurels.Awards;

/// <summary>
/// Polish names for the common award categories. Wikidata's English labels are the source of truth
/// (they are consistent across families); Polish labels on Wikidata are inconsistent in grammatical case,
/// so we translate the shortened English category ourselves and leave the rest in English.
/// </summary>
public static partial class CategoryNames
{
    private static readonly (Regex Pattern, string Pl)[] Table =
    [
        (Rx(@"^(Best )?(Motion )?Picture( – Drama)?$|^Best Film$|^Film$"), "Najlepszy film"),
        (Rx(@"Motion Picture – Musical or Comedy|Musical or Comedy"), "Najlepszy film (komedia lub musical)"),
        (Rx(@"^(Best )?(Direct(or|ing|ion))( – Motion Picture)?$"), "Najlepsza reżyseria"),
        (Rx(@"^(Best )?Actor( in a Leading Role)?( – Motion Picture – Drama)?$"), "Najlepszy aktor pierwszoplanowy"),
        (Rx(@"^(Best )?Actress( in a Leading Role)?( – Motion Picture – Drama)?$"), "Najlepsza aktorka pierwszoplanowa"),
        (Rx(@"Actor – Motion Picture – Musical or Comedy"), "Najlepszy aktor (komedia lub musical)"),
        (Rx(@"Actress – Motion Picture – Musical or Comedy"), "Najlepsza aktorka (komedia lub musical)"),
        (Rx(@"Supporting Actor"), "Najlepszy aktor drugoplanowy"),
        (Rx(@"Supporting Actress"), "Najlepsza aktorka drugoplanowa"),
        (Rx(@"Original Screenplay"), "Najlepszy scenariusz oryginalny"),
        (Rx(@"Adapted Screenplay"), "Najlepszy scenariusz adaptowany"),
        (Rx(@"^(Best )?Screenplay( – Motion Picture)?$|^Writing$"), "Najlepszy scenariusz"),
        (Rx(@"International Feature|Foreign Language Film|Non-English Language Film|Film Not in the English Language"), "Najlepszy film międzynarodowy"),
        (Rx(@"Animated Feature|Animated Film"), "Najlepszy film animowany"),
        (Rx(@"Animated Short"), "Najlepszy krótkometrażowy film animowany"),
        (Rx(@"Live Action Short"), "Najlepszy krótkometrażowy film aktorski"),
        (Rx(@"Documentary (Feature|Film)|^Documentary$"), "Najlepszy film dokumentalny"),
        (Rx(@"Documentary Short"), "Najlepszy krótkometrażowy film dokumentalny"),
        (Rx(@"Cinematography"), "Najlepsze zdjęcia"),
        (Rx(@"Film Editing|^Editing$"), "Najlepszy montaż"),
        (Rx(@"Original (Dramatic |Musical or Comedy )?Score|Original Music|^Music$|Original Song Score"), "Najlepsza muzyka"),
        (Rx(@"Original Song"), "Najlepsza piosenka"),
        (Rx(@"Visual Effects|Special Effects|Special Visual Effects"), "Najlepsze efekty specjalne"),
        (Rx(@"Production Design|Art Direction"), "Najlepsza scenografia"),
        (Rx(@"Costume Design"), "Najlepsze kostiumy"),
        (Rx(@"Makeup and Hairstyling|^Makeup$|Make Up"), "Najlepsza charakteryzacja"),
        (Rx(@"^(Best )?Sound( Mixing| Editing)?$|Sound Mixing|Sound Editing"), "Najlepszy dźwięk"),
        (Rx(@"^(Best )?Casting$"), "Najlepszy casting"),
        (Rx(@"Outstanding British Film|British Film"), "Najlepszy film brytyjski"),
        (Rx(@"Debut by a British"), "Najlepszy debiut brytyjski"),
        (Rx(@"Rising Star"), "Wschodząca gwiazda"),
        (Rx(@"Palme d'Or"), "Złota Palma"),
        (Rx(@"Grand Prix"), "Grand Prix"),
        (Rx(@"Jury Prize"), "Nagroda Jury"),
        (Rx(@"Caméra d'Or"), "Złota Kamera"),
        (Rx(@"Un Certain Regard"), "Un Certain Regard"),
        (Rx(@"Golden Bear"), "Złoty Niedźwiedź"),
        (Rx(@"Silver Bear.*Grand Jury"), "Srebrny Niedźwiedź – Grand Prix Jury"),
        (Rx(@"Silver Bear.*Director"), "Srebrny Niedźwiedź za reżyserię"),
        (Rx(@"Silver Bear.*(Actor|Actress|Leading Performance)"), "Srebrny Niedźwiedź za rolę"),
        (Rx(@"Silver Bear"), "Srebrny Niedźwiedź"),
        (Rx(@"Golden Lion"), "Złoty Lew"),
        (Rx(@"Silver Lion"), "Srebrny Lew"),
        (Rx(@"Volpi Cup.*Actor"), "Puchar Volpiego dla aktora"),
        (Rx(@"Volpi Cup.*Actress"), "Puchar Volpiego dla aktorki"),
        (Rx(@"Grand Jury Prize"), "Wielka Nagroda Jury"),
        (Rx(@"Audience Award"), "Nagroda Publiczności"),
        (Rx(@"Outstanding (Drama|Comedy|Limited|Anthology).*Series|Outstanding Limited or Anthology Series"), "Najlepszy serial"),
        (Rx(@"Television Series – Drama"), "Najlepszy serial dramatyczny"),
        (Rx(@"Television Series – Musical or Comedy"), "Najlepszy serial komediowy"),
        (Rx(@"Lead Actor"), "Najlepszy aktor pierwszoplanowy"),
        (Rx(@"Lead Actress"), "Najlepsza aktorka pierwszoplanowa"),
        (Rx(@"European Film$|^European Film"), "Najlepszy film europejski"),
        (Rx(@"European Director"), "Najlepszy reżyser"),
        (Rx(@"European Actor"), "Najlepszy aktor"),
        (Rx(@"European Actress"), "Najlepsza aktorka"),
        (Rx(@"European Screenwriter"), "Najlepszy scenariusz"),
        (Rx(@"Golden Leopard"), "Złoty Lampart"),
        (Rx(@"Golden Lions|Grand Prize.*Gdynia|Złote Lwy"), "Złote Lwy"),
        (Rx(@"People's Choice Award"), "Nagroda Publiczności"),
        (Rx(@"Outstanding Performance by a Cast|Ensemble"), "Najlepsza obsada"),
        (Rx(@"Outstanding Performance by a Male Actor in a Leading|Male Actor in a Leading Role"), "Najlepszy aktor pierwszoplanowy"),
        (Rx(@"Outstanding Performance by a Female Actor in a Leading|Female Actor in a Leading Role"), "Najlepsza aktorka pierwszoplanowa"),
        (Rx(@"Male Actor in a Supporting"), "Najlepszy aktor drugoplanowy"),
        (Rx(@"Female Actor in a Supporting"), "Najlepsza aktorka drugoplanowa"),
        (Rx(@"Stunt Ensemble"), "Najlepsze kaskaderstwo"),
        (Rx(@"Best Feature$|Best First Feature"), "Najlepszy film"),
        (Rx(@"Science Fiction Film"), "Najlepszy film science fiction"),
        (Rx(@"Fantasy Film"), "Najlepszy film fantasy"),
        (Rx(@"Horror Film"), "Najlepszy horror"),
        (Rx(@"Action.*Film|Thriller Film"), "Najlepszy film akcji"),
        (Rx(@"Worst Picture"), "Najgorszy film"),
        (Rx(@"Worst Director"), "Najgorsza reżyseria"),
        (Rx(@"Worst Actor"), "Najgorszy aktor"),
        (Rx(@"Worst Actress"), "Najgorsza aktorka"),
        (Rx(@"Worst Screenplay"), "Najgorszy scenariusz"),
    ];

    private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Translates a shortened English category when the current language is Polish; otherwise returns it unchanged.</summary>
    public static string Localize(string shortCategory)
    {
        if (!AwardText.Language.StartsWith("pl", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(shortCategory))
        {
            return shortCategory;
        }

        foreach (var (pattern, pl) in Table)
        {
            if (pattern.IsMatch(shortCategory))
            {
                return pl;
            }
        }

        return shortCategory;
    }
}
