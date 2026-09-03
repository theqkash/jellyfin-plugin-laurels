using Jellyfin.Plugin.Laurels.Awards;
using Jellyfin.Plugin.Laurels.Ratings;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.Laurels.Sections;

/// <summary>
/// Produces the items for a Laurels home screen section. Instantiated by Home Screen Sections through
/// the host container (constructor parameters must be host-registered services).
/// </summary>
public sealed class SectionResultsHandler
{
    private const int MaxItems = 16;

    /// <summary>Year of the newest (winning) award of the family; falls back to the item's year when Wikidata has no date.</summary>
    private int YearOf(TitleAwards t, string family, bool wonOnly)
    {
        var years = t.Awards.Where(a => (!wonOnly || a.Won) && (family == "*" || a.Family == family) && a.Year.HasValue).Select(a => a.Year!.Value).ToList();
        if (years.Count > 0)
        {
            return years.Max();
        }

        return (_libraryManager.GetItemById(t.ItemId)?.ProductionYear ?? 0) + 1;
    }
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IDtoService _dtoService;
    private readonly AwardsIndex _awards;
    private readonly RatingsCache _ratings;

    public SectionResultsHandler(ILibraryManager libraryManager, IUserManager userManager, IDtoService dtoService, AwardsIndex awards, RatingsCache ratings)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _dtoService = dtoService;
        _awards = awards;
        _ratings = ratings;
    }

    /// <summary>
    /// Entry point called by Home Screen Sections. AdditionalData carries the section key.
    /// </summary>
    public QueryResult<BaseItemDto> GetResults(SectionPayload payload)
    {
        var user = _userManager.GetUserById(payload.UserId);
        var definition = SectionCatalog.Resolve(payload.AdditionalData ?? string.Empty);
        if (user is null || definition is null)
        {
            return new QueryResult<BaseItemDto>();
        }

        var enabledFamilies = AwardFamilies.FromCsv(Plugin.Instance?.Configuration.AwardFamilies).Select(f => f.Key).ToList();
        IEnumerable<Guid> ids = definition.Kind switch
        {
            "winners" => _awards.Winners(definition.Argument == "*" ? enabledFamilies : [definition.Argument])
                .OrderByDescending(t => YearOf(t, definition.Argument, wonOnly: true))
                .ThenByDescending(t => t.Awards.Count(a => a.Won && (definition.Argument == "*" || a.Family == definition.Argument)))
                .Select(t => t.ItemId),
            "nominees" => _awards.Nominees(definition.Argument == "*" ? enabledFamilies : [definition.Argument])
                .OrderByDescending(t => YearOf(t, definition.Argument, wonOnly: false))
                .ThenByDescending(t => t.Awards.Count(a => definition.Argument == "*" || a.Family == definition.Argument))
                .Select(t => t.ItemId),
            "top" => _ratings.All
                .Select(t => (t.ItemId, Rating: t.Find(definition.Argument)))
                .Where(x => x.Rating?.Score is not null && (x.Rating.Votes ?? int.MaxValue) >= 500)
                .OrderByDescending(x => x.Rating!.Score)
                .Select(x => x.ItemId),
            _ => [],
        };

        var items = new List<BaseItem>();
        foreach (var id in ids)
        {
            var item = _libraryManager.GetItemById(id);
            if (item is null || !item.IsVisible(user))
            {
                continue;
            }

            items.Add(item);
            if (items.Count >= MaxItems)
            {
                break;
            }
        }

        var dtoOptions = new DtoOptions
        {
            Fields = [ItemFields.PrimaryImageAspectRatio, ItemFields.MediaSourceCount],
            ImageTypes = [ImageType.Primary, ImageType.Backdrop, ImageType.Thumb],
            ImageTypeLimit = 1,
        };
        return new QueryResult<BaseItemDto>(_dtoService.GetBaseItemDtos(items, dtoOptions, user));
    }
}
