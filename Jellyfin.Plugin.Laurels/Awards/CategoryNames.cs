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
        (Rx(@"Worst Picture"), "Najgorszy film"),
        (Rx(@"Worst Director"), "Najgorsza reżyseria"),
        (Rx(@"Worst Actor"), "Najgorszy aktor"),
        (Rx(@"Worst Actress"), "Najgorsza aktorka"),
        (Rx(@"Worst Screenplay"), "Najgorszy scenariusz"),
        (Rx(@"Worst Prequel|Remake, Rip-off or Sequel"), "Najgorszy prequel, remake lub sequel"),
        (Rx(@"Worst Screen (Combo|Couple)"), "Najgorszy duet"),
        (Rx(@"Achievement in World Cinema"), "Nagroda za wkład w kino światowe"),
        (Rx(@"Actress in an Action Movie"), "Najlepsza aktorka w filmie akcji"),
        (Rx(@"Actor – Television Series Drama"), "Najlepszy aktor w serialu dramatycznym"),
        (Rx(@"Actress – Television Series Drama"), "Najlepsza aktorka w serialu dramatycznym"),
        (Rx(@"Worst New Star"), "Najgorsza nowa gwiazda"),
        (Rx(@"Actor in an Action Movie"), "Najlepszy aktor w filmie akcji"),
        (Rx(@"Foreign Director"), "Najlepszy reżyser zagraniczny"),
        (Rx(@"Foreign Actor"), "Najlepszy aktor zagraniczny"),
        (Rx(@"Foreign Actress"), "Najlepsza aktorka zagraniczna"),
        (Rx(@"Ibero-American Film"), "Najlepszy film iberoamerykański"),
        (Rx(@"Foreign Film|International Film|No-German Film"), "Najlepszy film zagraniczny"),
        (Rx(@"Female Actor in a Supporting|Supporting Female"), "Najlepsza aktorka drugoplanowa"),
        (Rx(@"Actor in a Supporting Role|Supporting Male"), "Najlepszy aktor drugoplanowy"),
        (Rx(@"^(Best )?Supporting Performance$"), "Najlepsza rola drugoplanowa"),
        (Rx(@"^(Best )?Leading Performance$"), "Najlepsza rola pierwszoplanowa"),
        (Rx(@"Actress in a Supporting Role"), "Najlepsza aktorka drugoplanowa"),
        (Rx(@"Documentary \(Short Subject\)"), "Najlepszy krótkometrażowy film dokumentalny"),
        (Rx(@"^(Best )?(Motion )?Picture( – Drama)?$|^Best Film$|^Film$"), "Najlepszy film"),
        (Rx(@"Motion Picture – Musical or Comedy|Musical or Comedy"), "Najlepszy film (komedia lub musical)"),
        (Rx(@"^(Best )?(Direct(or|ing|ion))( – Motion Picture)?$"), "Najlepsza reżyseria"),
        (Rx(@"^(Best )?Actor( in a Leading Role)?( – Motion Picture( –)? Drama)?$"), "Najlepszy aktor pierwszoplanowy"),
        (Rx(@"^(Best )?Actress( in a Leading Role)?( – Motion Picture( –)? Drama)?$"), "Najlepsza aktorka pierwszoplanowa"),
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
        (Rx(@"First Feature"), "Najlepszy debiut"),
        (Rx(@"Best Feature$"), "Najlepszy film"),
        (Rx(@"Science Fiction Film"), "Najlepszy film science fiction"),
        (Rx(@"Fantasy Film"), "Najlepszy film fantasy"),
        (Rx(@"Horror Film"), "Najlepszy horror"),
        (Rx(@"Action.*(Film|Movie)|Thriller Film"), "Najlepszy film akcji"),
        (Rx(@"Sci-Fi/Horror"), "Najlepszy film science fiction lub horror"),
        (Rx(@"^(Best )?Feature Film$|^Best Feature-Length Film$"), "Najlepszy film"),
        (Rx(@"Children.s (&|and) Family Film|Family Film"), "Najlepszy film familijny"),
        (Rx(@"Children.s Animated Program"), "Najlepszy program animowany dla dzieci"),
        (Rx(@"^(Best )?Comedy( Film)?$"), "Najlepsza komedia"),
        (Rx(@"Independent Film"), "Najlepszy film niezależny"),
        (Rx(@"Comic-to-Film"), "Najlepsza adaptacja komiksu"),
        (Rx(@"^Picture of the Year$"), "Film roku"),
        (Rx(@"^Animation of the Year$"), "Animacja roku"),
        (Rx(@"^Director of the Year$"), "Reżyser roku"),
        (Rx(@"^Screenplay of the Year$"), "Scenariusz roku"),
        (Rx(@"Actress in a Leading Role|Female Lead"), "Najlepsza aktorka pierwszoplanowa"),
        (Rx(@"Actor in a Leading Role|Male Lead"), "Najlepszy aktor pierwszoplanowy"),
        (Rx(@"Young Performer"), "Najlepszy młody aktor"),
        (Rx(@"Voice Acting"), "Najlepsza rola głosowa"),
        (Rx(@"^Acting$"), "Nagroda aktorska"),
        (Rx(@"^(Best )?Cast$"), "Najlepsza obsada"),
        (Rx(@"First Screenplay"), "Najlepszy debiut scenariuszowy"),
        (Rx(@"Screenwriter|^(Best )?Writing$|Writing (in|for) "), "Najlepszy scenariusz"),
        (Rx(@"Direct(ing|or)"), "Najlepsza reżyseria"),
        (Rx(@"Cinematographer"), "Najlepsze zdjęcia"),
        (Rx(@"^(Best )?Edit(ing|or)$|Picture Editing"), "Najlepszy montaż"),
        (Rx(@"Composer|Music|Score"), "Najlepsza muzyka"),
        (Rx(@"^(Best )?Song$"), "Najlepsza piosenka"),
        (Rx(@"Costume"), "Najlepsze kostiumy"),
        (Rx(@"Make-?up|Hair"), "Najlepsza charakteryzacja"),
        (Rx(@"Set Decoration"), "Najlepsza scenografia"),
        (Rx(@"Sound"), "Najlepszy dźwięk"),
        (Rx(@"^(Best )?Documentary$|Documentary Award"), "Najlepszy film dokumentalny"),
        (Rx(@"Character Animation"), "Najlepsza animacja postaci"),
        (Rx(@"Character Design"), "Najlepszy projekt postaci"),
        (Rx(@"Storyboard"), "Najlepszy storyboard"),
        (Rx(@"Main Title Design"), "Najlepsza czołówka"),
        (Rx(@"Limited or Anthology Series or Television Film|Movie/Miniseries"), "Najlepszy miniserial lub film telewizyjny"),
        (Rx(@"Telenovela"), "Najlepsza telenowela"),
        (Rx(@"Television Presentation"), "Najlepsza produkcja telewizyjna"),
        (Rx(@"DVD Release"), "Najlepsze wydanie DVD"),
        (Rx(@"Production Supervision"), "Najlepsze kierownictwo produkcji"),
        (Rx(@"Discovery of the Year|Young European Film"), "Odkrycie roku"),
        (Rx(@"^Best First Film$"), "Najlepszy debiut"),
        (Rx(@"Newcomer|Revelation|New Actor|New Actress"), "Najlepszy debiut aktorski"),
        (Rx(@"University Film"), "Europejska Nagroda Uniwersytecka"),
        (Rx(@"LUX"), "Nagroda publiczności LUX"),
        (Rx(@"FIPRESCI"), "Nagroda FIPRESCI"),
        (Rx(@"Technical Artist"), "Nagroda techniczna CST"),
        (Rx(@"Special Jury"), "Specjalna nagroda jury"),
        (Rx(@"Special Achievement"), "Nagroda specjalna"),
        (Rx(@"Producers Award"), "Nagroda dla producentów"),
        (Rx(@"Animated Program"), "Najlepszy program animowany"),
        (Rx(@"Short Film"), "Najlepszy film krótkometrażowy"),
        (Rx(@"Casting"), "Najlepszy casting"),
        (Rx(@"Documentary"), "Najlepszy film dokumentalny"),
        (Rx(@"Audience|of the public|Festival Favorite"), "Nagroda publiczności"),
        (Rx(@"^Teddy$"), "Nagroda Teddy"),
        (Rx(@"Editing"), "Najlepszy montaż"),
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
                return shortCategory.StartsWith("Worst", StringComparison.OrdinalIgnoreCase) && pl.StartsWith("Najlepsz", StringComparison.Ordinal)
                    ? "Najgorsz" + pl["Najlepsz".Length..]
                    : pl;
            }
        }

        return shortCategory;
    }
}
