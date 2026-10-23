namespace Manifesto.RazorKit.Models;

/// <summary>
/// Configuration options for RazorKit
/// </summary>
public class RazorKitOptions
{
    /// <summary>
    /// The name of the component library to use in static file paths (e.g., "MyProjectName.UmbracoComponents")
    /// This is used to construct paths like "/_content/{ComponentLibraryName}/css/main.css"
    /// </summary>
    public string ComponentLibraryName { get; set; } = string.Empty;

    /// <summary>
    /// The public origins the explorer is served from, such as "https://www.example.com". RazorKit
    /// accepts a render request from these origins when the browser does not send Sec-Fetch-Site.
    /// When empty, the origin is derived from the request's own scheme and host, which is wrong
    /// behind a proxy or CDN that terminates TLS unless forwarded headers are configured.
    /// Each entry must be scheme://host[:port], with no path.
    /// </summary>
    public IList<string> AllowedOrigins { get; set; } = new List<string>();

    /// <summary>
    /// The largest props JSON a render request may carry, in UTF-8 bytes. Larger requests get a 413.
    /// </summary>
    public int MaxPropsJsonBytes { get; set; } = DefaultMaxPropsJsonBytes;

    internal const int DefaultMaxPropsJsonBytes = 64 * 1024;

    // Keeps the derived body cap (a multiple of this) well inside int and long arithmetic.
    internal const int MaxMaxPropsJsonBytes = 16 * 1024 * 1024;
}
