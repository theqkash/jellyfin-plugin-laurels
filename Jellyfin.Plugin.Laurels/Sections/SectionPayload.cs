namespace Jellyfin.Plugin.Laurels.Sections;

/// <summary>
/// Shape of the payload Home Screen Sections sends when it asks a plugin-defined section for results.
/// </summary>
public sealed class SectionPayload
{
    public Guid UserId { get; set; }

    public string? AdditionalData { get; set; }
}
