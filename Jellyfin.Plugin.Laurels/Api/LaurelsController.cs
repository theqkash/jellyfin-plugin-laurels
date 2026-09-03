using System.Net.Mime;
using System.Reflection;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Laurels.Awards;
using Jellyfin.Plugin.Laurels.Configuration;
using Jellyfin.Plugin.Laurels.Model;
using Jellyfin.Plugin.Laurels.Ratings;
using Jellyfin.Plugin.Laurels.Reviews;
using Jellyfin.Plugin.Laurels.Web;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Laurels.Api;

/// <summary>
/// HTTP API used by the injected web client and by the settings page.
/// </summary>
[ApiController]
[Route("Laurels")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class LaurelsController : ControllerBase
{
    private const string ResourcePrefix = "Jellyfin.Plugin.Laurels.Web.";
    private readonly RatingsService _ratings;
    private readonly MdbListClient _mdbList;
    private readonly ILibraryManager _libraryManager;
    private readonly AwardsService _awards;
    private readonly RatingsCache _cache;
    private readonly TmdbReviewsClient _reviews;

    public LaurelsController(RatingsService ratings, MdbListClient mdbList, ILibraryManager libraryManager, AwardsService awards, RatingsCache cache, TmdbReviewsClient reviews)
    {
        _reviews = reviews;
        _ratings = ratings;
        _mdbList = mdbList;
        _libraryManager = libraryManager;
        _awards = awards;
        _cache = cache;
    }

    // ---- Static files for the web client (no auth: index.html loads them before login) ----

    [HttpGet("Client/{fileName}")]
    [AllowAnonymous]
    [ResponseCache(Duration = 3600)]
    public IActionResult GetClientFile(string fileName) => ServeEmbedded("Client." + fileName);

    [HttpGet("Assets/{fileName}")]
    [AllowAnonymous]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetAsset(string fileName) => ServeEmbedded("Assets." + fileName);

    /// <summary>Settings the client needs (no secrets).</summary>
    [HttpGet("ClientSettings")]
    [AllowAnonymous]
    public ActionResult<ClientSettingsDto> GetClientSettings()
    {
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return new ClientSettingsDto
        {
            ReplaceRatingIcons = cfg.ReplaceRatingIcons,
            ShowRatingsPanel = cfg.ShowRatingsPanel,
            RatingsPanelSources = SplitCsv(cfg.RatingsPanelSources),
            ShowAwardBadges = cfg.ShowAwardBadges && cfg.EnableAwards,
            ShowReviewExcerpts = cfg.ShowReviewExcerpts,
            Sources = RatingSources.All.Select(s => new SourceDto { Key = s.Key, Name = s.DisplayName, Icon = s.Icon, Max = s.NativeMax }).ToList(),
            Version = Plugin.Instance?.Version.ToString() ?? "0",
            Words = new Dictionary<string, string>
            {
                ["winner"] = AwardText.Winner,
                ["nominee"] = AwardText.Nominee,
                ["wins2"] = AwardText.Wins(2),
                ["noms2"] = AwardText.Nominations(2),
            },
        };
    }

    // ---- Per item data ----

    [HttpGet("Items/{itemId}")]
    [Authorize]
    public ActionResult<ItemRatingsDto> GetItem([FromRoute] Guid itemId)
    {
        var cached = _ratings.GetCached(itemId);
        if (cached is null)
        {
            return new ItemRatingsDto { ItemId = itemId, Found = false };
        }

        return new ItemRatingsDto
        {
            ItemId = itemId,
            Found = true,
            FetchedUtc = cached.FetchedUtc,
            Ids = cached.Ids,
            Ratings = cached.Ratings.Where(r => r.Score.HasValue).ToList(),
            Flags = cached.Flags,
            AppliedCommunitySource = cached.AppliedCommunitySource,
            AppliedCriticSource = cached.AppliedCriticSource,
        };
    }

    /// <summary>Forces a fresh fetch for one item (used by the "refresh" button and for debugging).</summary>
    [HttpPost("Items/{itemId}/Refresh")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<RefreshDto>> RefreshItem([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var outcome = await _ratings.RefreshAsync(item, force: true, cancellationToken).ConfigureAwait(false);
        return new RefreshDto { Outcome = outcome.ToString(), Ratings = _ratings.GetCached(itemId)?.Ratings ?? [] };
    }

    /// <summary>Awards of one item, grouped by family (what the badges show).</summary>
    [HttpGet("Awards/{itemId}")]
    [Authorize]
    public ActionResult<ItemAwardsDto> GetAwards([FromRoute] Guid itemId)
    {
        var entry = _awards.Index.Get(itemId);
        var dto = new ItemAwardsDto { ItemId = itemId, Found = entry is not null };
        if (entry is null)
        {
            return dto;
        }

        var enabled = _awards.EnabledFamilies;
        foreach (var group in entry.Awards.GroupBy(a => a.Family))
        {
            var family = AwardFamilies.Get(group.Key);
            if (family is null || !enabled.Contains(family))
            {
                continue;
            }

            var wins = group.Count(a => a.Won);
            dto.Entries.Add(new AwardGroupDto
            {
                FamilyKey = family.Key,
                Family = family.ShortName,
                FamilyName = AwardText.FamilyName(family),
                Won = wins > 0,
                Wins = wins,
                Nominations = group.Count(),
                Year = group.Max(a => a.Year),
                Categories = group.OrderByDescending(a => a.Won).ThenBy(a => AwardText.CategoryRank(AwardText.ShortCategory(a.Category))).Select(a => (a.Won ? "★ " : "○ ") + AwardText.ShortCategory(a.Category) + (a.Year.HasValue ? " (" + a.Year + ")" : string.Empty)).ToList(),
                WonCategories = group.Where(a => a.Won).Select(a => AwardText.ShortCategory(a.Category)).Distinct().OrderBy(AwardText.CategoryRank).ToList(),
            });
        }

        dto.Entries = dto.Entries.OrderByDescending(e => e.Won).ThenByDescending(e => e.Wins).ThenByDescending(e => e.Nominations).ToList();
        return dto;
    }

    /// <summary>Review excerpts for one item (TMDb; empty when no TMDb key is configured).</summary>
    [HttpGet("Reviews/{itemId}")]
    [Authorize]
    public async Task<ActionResult<TitleReviews>> GetReviews([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null || !RatingsService.IsSupported(item))
        {
            return new TitleReviews { ItemId = itemId, FetchedUtc = DateTimeOffset.UtcNow };
        }

        return await _reviews.GetAsync(item, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Captions for home screen cards: what a title won / how it is rated, per section.</summary>
    [HttpPost("Cards")]
    [Authorize]
    public ActionResult<CardsDto> GetCards([FromBody] CardsRequestDto body)
    {
        var dto = new CardsDto();
        var definition = Sections.SectionCatalog.Resolve(body.Section ?? string.Empty);
        foreach (var id in body.ItemIds.Distinct().Take(64))
        {
            string? caption = null;
            string? tooltip = null;
            if (definition is null || definition.Kind is "winners" or "nominees")
            {
                var entry = _awards.Index.Get(id);
                var family = definition is null || definition.Argument == "*" ? null : definition.Argument;
                var records = entry?.Awards.Where(a => family is null || a.Family == family).OrderByDescending(a => a.Won).ThenByDescending(a => a.Year).ToList() ?? [];
                if (records.Count > 0)
                {
                    var wins = records.Where(a => a.Won).ToList();
                    var shown = (wins.Count > 0 ? wins : records).Select(a => AwardText.ShortCategory(a.Category)).Distinct().OrderBy(AwardText.CategoryRank).Take(2).ToList();
                    var fam = AwardFamilies.Get(records[0].Family);
                    var year = records[0].Year;
                    var head = (family is null && fam is not null ? AwardText.FamilyName(fam) + " " : string.Empty) + (year.HasValue ? year.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
                    var more = (wins.Count > 0 ? wins.Count : records.Count) - shown.Count;
                    caption = (head.Length > 0 ? head + " · " : string.Empty) + string.Join(", ", shown) + (more > 0 ? " +" + more : string.Empty);
                    tooltip = string.Join("\n", records.OrderByDescending(a => a.Won).ThenBy(a => AwardText.CategoryRank(AwardText.ShortCategory(a.Category))).Select(a => (a.Won ? "★ " : "○ ") + AwardText.ShortCategory(a.Category) + (a.Year.HasValue ? " (" + a.Year + ")" : string.Empty)));
                }
            }
            else if (definition.Kind == "top")
            {
                var r = _ratings.GetCached(id)?.Find(definition.Argument);
                var src = RatingSources.Get(definition.Argument);
                if (r is not null && src is not null)
                {
                    var value = src.NativeMax == 100 ? Math.Round(r.Value ?? r.Score ?? 0) + "%" : (r.Value ?? 0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                    caption = src.DisplayName + " " + value + (r.Votes.HasValue ? " (" + FormatVotes(r.Votes.Value) + ")" : string.Empty);
                }
            }

            if (caption is not null)
            {
                dto.Items[id] = new CardDto { Caption = caption, Tooltip = tooltip };
            }
        }

        return dto;
    }

    /// <summary>Sections Laurels registered, with the collection each one links to (if the awards library is on).</summary>
    [HttpGet("Sections")]
    [Authorize]
    public ActionResult<List<SectionDto>> GetSections()
    {
        var keys = Sections.SectionCatalog.ConfiguredKeys();
        var list = new List<SectionDto>();
        foreach (var key in keys)
        {
            var def = Sections.SectionCatalog.Resolve(key);
            if (def is null)
            {
                continue;
            }

            var box = def.Kind is "winners" or "nominees" && def.Argument != "*" ? _awards.Library.FindCollection(def.Kind + ":" + def.Argument) : null;
            list.Add(new SectionDto { Id = Sections.SectionCatalog.SectionId(def.Key), Key = def.Key, Title = def.DisplayText, CollectionId = box?.Id });
        }

        return list;
    }

    /// <summary>Creates/syncs the awards library and its collections now (settings page button).</summary>
    [HttpPost("Library/Sync")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public IActionResult SyncLibrary()
    {
        _ = Task.Run(() => _awards.Library.SyncAsync(CancellationToken.None));
        return NoContent();
    }

    [HttpGet("Library")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<List<LibraryStateDto>> GetLibraryState() =>
        _awards.Library.GetState().Select(s => new LibraryStateDto { Key = s.Key, Title = s.Title, CollectionId = s.CollectionId, ItemCount = s.ItemCount }).ToList();

    private static string FormatVotes(int v) => v >= 1_000_000 ? (v / 1_000_000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M" : v >= 1000 ? (v / 1000) + "k" : v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Kicks off a full awards refresh in the background (settings page button).</summary>
    [HttpPost("Awards/Refresh")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public IActionResult RefreshAwards([FromQuery] bool force = false)
    {
        _ = Task.Run(() => _awards.RefreshAllAsync(force, null, CancellationToken.None));
        return NoContent();
    }

    // ---- Settings page helpers ----

    [HttpGet("Status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<StatusDto> GetStatus()
    {
        return new StatusDto
        {
            FileTransformationInstalled = FileTransformationBridge.IsInstalled(),
            WebUiActive = WebTransformHost.IsActive,
            CachedItems = _cache.Count,
            IndexedAwards = _awards.Index.Count,
            HomeScreenSectionsInstalled = Sections.HomeSectionsBridge.IsInstalled(),
            RegisteredSections = Tasks.StartupTask.RegisteredSections,
            Version = Plugin.Instance?.Version.ToString() ?? "0",
        };
    }

    [HttpPost("TestMdbList")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<TestDto>> TestMdbList([FromBody] TestRequestDto body, CancellationToken cancellationToken)
    {
        // The Shawshank Redemption, TMDb 278.
        var result = await _mdbList.GetAsync("tmdb", "movie", "278", body.ApiKey ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return new TestDto
        {
            Ok = result.Success,
            Message = result.Success
                ? $"OK, {result.Ratings.Count} ratings returned" + (result.RateLimitRemaining.HasValue ? $", {result.RateLimitRemaining} requests left today" : string.Empty)
                : result.InvalidKey ? "MDBList rejected the API key" : result.RateLimited ? "Rate limit reached" : result.Error ?? "failed",
        };
    }

    private IActionResult ServeEmbedded(string relative)
    {
        if (relative.Contains("..", StringComparison.Ordinal) || relative.Contains('/', StringComparison.Ordinal))
        {
            return BadRequest();
        }

        var assembly = Assembly.GetExecutingAssembly();
        var name = ResourcePrefix + relative;
        var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return NotFound();
        }

        var contentType = Path.GetExtension(relative).ToLowerInvariant() switch
        {
            ".js" => "application/javascript",
            ".css" => "text/css",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".json" => MediaTypeNames.Application.Json,
            _ => MediaTypeNames.Application.Octet,
        };
        return File(stream, contentType);
    }

    private static List<string> SplitCsv(string? csv) =>
        (csv ?? string.Empty).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(RatingSources.Normalize).Distinct().ToList();

    public sealed class ClientSettingsDto
    {
        [JsonPropertyName("replaceRatingIcons")]
        public bool ReplaceRatingIcons { get; set; }

        [JsonPropertyName("showRatingsPanel")]
        public bool ShowRatingsPanel { get; set; }

        [JsonPropertyName("ratingsPanelSources")]
        public List<string> RatingsPanelSources { get; set; } = [];

        [JsonPropertyName("showAwardBadges")]
        public bool ShowAwardBadges { get; set; }

        [JsonPropertyName("showReviewExcerpts")]
        public bool ShowReviewExcerpts { get; set; }

        [JsonPropertyName("sources")]
        public List<SourceDto> Sources { get; set; } = [];

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("words")]
        public Dictionary<string, string> Words { get; set; } = new();
    }

    public sealed class SourceDto
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("icon")]
        public string Icon { get; set; } = string.Empty;

        [JsonPropertyName("max")]
        public double Max { get; set; }
    }

    public sealed class ItemRatingsDto
    {
        [JsonPropertyName("itemId")]
        public Guid ItemId { get; set; }

        [JsonPropertyName("found")]
        public bool Found { get; set; }

        [JsonPropertyName("fetchedUtc")]
        public DateTimeOffset? FetchedUtc { get; set; }

        [JsonPropertyName("ids")]
        public Dictionary<string, string> Ids { get; set; } = new();

        [JsonPropertyName("ratings")]
        public List<RatingEntry> Ratings { get; set; } = [];

        [JsonPropertyName("flags")]
        public List<string> Flags { get; set; } = [];

        [JsonPropertyName("appliedCommunitySource")]
        public string? AppliedCommunitySource { get; set; }

        [JsonPropertyName("appliedCriticSource")]
        public string? AppliedCriticSource { get; set; }
    }

    public sealed class RefreshDto
    {
        [JsonPropertyName("outcome")]
        public string Outcome { get; set; } = string.Empty;

        [JsonPropertyName("ratings")]
        public List<RatingEntry> Ratings { get; set; } = [];
    }

    public sealed class StatusDto
    {
        [JsonPropertyName("fileTransformationInstalled")]
        public bool FileTransformationInstalled { get; set; }

        [JsonPropertyName("webUiActive")]
        public bool WebUiActive { get; set; }

        [JsonPropertyName("cachedItems")]
        public int CachedItems { get; set; }

        [JsonPropertyName("indexedAwards")]
        public int IndexedAwards { get; set; }

        [JsonPropertyName("homeScreenSectionsInstalled")]
        public bool HomeScreenSectionsInstalled { get; set; }

        [JsonPropertyName("registeredSections")]
        public int RegisteredSections { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
    }

    public sealed class ItemAwardsDto
    {
        [JsonPropertyName("itemId")]
        public Guid ItemId { get; set; }

        [JsonPropertyName("found")]
        public bool Found { get; set; }

        [JsonPropertyName("entries")]
        public List<AwardGroupDto> Entries { get; set; } = [];
    }

    public sealed class AwardGroupDto
    {
        [JsonPropertyName("familyKey")]
        public string FamilyKey { get; set; } = string.Empty;

        [JsonPropertyName("family")]
        public string Family { get; set; } = string.Empty;

        [JsonPropertyName("familyName")]
        public string FamilyName { get; set; } = string.Empty;

        [JsonPropertyName("won")]
        public bool Won { get; set; }

        [JsonPropertyName("wins")]
        public int Wins { get; set; }

        [JsonPropertyName("nominations")]
        public int Nominations { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = [];

        [JsonPropertyName("wonCategories")]
        public List<string> WonCategories { get; set; } = [];
    }

    public sealed class CardsRequestDto
    {
        [JsonPropertyName("section")]
        public string? Section { get; set; }

        [JsonPropertyName("itemIds")]
        public List<Guid> ItemIds { get; set; } = [];
    }

    public sealed class CardsDto
    {
        [JsonPropertyName("items")]
        public Dictionary<Guid, CardDto> Items { get; set; } = new();
    }

    public sealed class CardDto
    {
        [JsonPropertyName("caption")]
        public string Caption { get; set; } = string.Empty;

        [JsonPropertyName("tooltip")]
        public string? Tooltip { get; set; }
    }

    public sealed class SectionDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("collectionId")]
        public Guid? CollectionId { get; set; }
    }

    public sealed class LibraryStateDto
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("collectionId")]
        public Guid? CollectionId { get; set; }

        [JsonPropertyName("itemCount")]
        public int ItemCount { get; set; }
    }

    public sealed class TestRequestDto
    {
        [JsonPropertyName("apiKey")]
        public string? ApiKey { get; set; }
    }

    public sealed class TestDto
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }
}
