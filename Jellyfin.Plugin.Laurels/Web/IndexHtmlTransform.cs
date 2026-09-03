using System.Text;

namespace Jellyfin.Plugin.Laurels.Web;

/// <summary>
/// Rewrites jellyfin-web's index.html in memory so it loads our client script.
/// </summary>
public static class IndexHtmlTransform
{
    private const string Marker = "data-laurels";

    /// <summary>Gets the script tag we inject. Relative URL so reverse-proxy sub-paths keep working.</summary>
    public static string ScriptTag =>
        $"<script {Marker} src=\"../Laurels/Client/laurels.js?v={Plugin.Instance?.Version.ToString() ?? "0"}\" defer></script>";

    public static string Apply(string html)
    {
        if (string.IsNullOrEmpty(html) || html.Contains(Marker, StringComparison.Ordinal))
        {
            return html;
        }

        var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? html + ScriptTag : html.Insert(idx, ScriptTag);
    }

    /// <summary>
    /// Signature required by File Transformation's TransformFile delegate: Task (string path, Stream contents).
    /// </summary>
    public static async Task TransformAsync(string path, Stream contents)
    {
        string html;
        using (var reader = new StreamReader(contents, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        {
            html = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        var transformed = Apply(html);
        contents.Seek(0, SeekOrigin.Begin);
        contents.SetLength(0);
        var bytes = Encoding.UTF8.GetBytes(transformed);
        await contents.WriteAsync(bytes).ConfigureAwait(false);
        contents.Seek(0, SeekOrigin.Begin);
    }
}
