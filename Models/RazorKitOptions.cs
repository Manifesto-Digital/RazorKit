namespace Manifesto.RazorKit.Models;

/// <summary>
/// Configuration options for RazorKit
/// </summary>
public class RazorKitOptions
{
    /// <summary>
    /// The name of the component library to use in static file paths (e.g., "MyProjectName.UmbracoComponents")
    /// This is used to construct paths like "/_content/{ComponentLibraryName}/css/main.css"
    /// when <see cref="PreviewStylesheets"/> or <see cref="PreviewScripts"/> is left null.
    /// </summary>
    public string ComponentLibraryName { get; set; } = string.Empty;

    // The three asset lists below default to null rather than to a filled list. Configuration
    // binding adds to a list that already has items, so a default of ["/css/main.css"] could
    // never be replaced from appsettings.json, only appended to.

    /// <summary>
    /// The site stylesheets the explorer page loads before its own, in order. Null (the default)
    /// loads "/css/main.css". An empty list loads none. Each entry is an app-relative path,
    /// "/x" or "~/x", resolved under the host's path base; anything else stops startup.
    /// </summary>
    public IList<string>? ExplorerStylesheets { get; set; }

    /// <summary>
    /// The stylesheets the preview loads, in order. Null (the default) loads
    /// "/_content/{ComponentLibraryName}/css/main.css", or none when ComponentLibraryName is empty.
    /// An empty list loads none. Paths follow the same rules as <see cref="ExplorerStylesheets"/>.
    /// </summary>
    public IList<string>? PreviewStylesheets { get; set; }

    /// <summary>
    /// The scripts the preview loads at the end of its body, in order. Null (the default) loads
    /// "/_content/{ComponentLibraryName}/js/main.js", or none when ComponentLibraryName is empty.
    /// An empty list loads none. Paths follow the same rules as <see cref="ExplorerStylesheets"/>.
    /// </summary>
    public IList<string>? PreviewScripts { get; set; }

    /// <summary>
    /// The public origins the explorer is served from, such as "https://www.example.com". RazorKit
    /// accepts a render request from these origins when the browser does not send Sec-Fetch-Site.
    /// When empty, the origin is derived from the request's own scheme and host, which is wrong
    /// behind a proxy or CDN that terminates TLS unless forwarded headers are configured.
    /// Each entry must be scheme://host[:port], with no path.
    /// </summary>
    public IList<string> AllowedOrigins { get; set; } = new List<string>();

    /// <summary>
    /// The themes the preview can be rendered in; the first is the default. With none, the preview
    /// sets no theme. With one, it is always applied. With two or more, the explorer shows a theme
    /// dropdown.
    /// </summary>
    public IList<RazorKitTheme> Themes { get; set; } = new List<RazorKitTheme>();

    /// <summary>
    /// The largest props JSON a render request may carry, in UTF-8 bytes. Larger requests get a 413.
    /// </summary>
    public int MaxPropsJsonBytes { get; set; } = DefaultMaxPropsJsonBytes;

    internal const int DefaultMaxPropsJsonBytes = 64 * 1024;

    // Keeps the derived body cap (a multiple of this) well inside int and long arithmetic.
    internal const int MaxMaxPropsJsonBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Per-client request limits for RazorKit's own endpoints. Enforced by RazorKit itself, so they
    /// apply whether or not the host registers ASP.NET Core's rate limiter.
    /// </summary>
    public RazorKitRateLimitOptions RateLimit { get; } = new();
}

/// <summary>
/// Request limits for RazorKit's endpoints. Clients are told apart by the connection's remote
/// address only. A host behind a proxy must configure forwarded headers itself, or every visitor
/// shares the proxy's address and therefore one bucket.
/// </summary>
public class RazorKitRateLimitOptions
{
    /// <summary>
    /// Limit for page loads: the explorer shell and GET previews. Each story a user opens costs two
    /// (shell and preview), and behind a proxy without forwarded headers every visitor shares one
    /// bucket, so the default is generous; <see cref="MaxConcurrentRenders"/> bounds the render work.
    /// </summary>
    public RazorKitBucketOptions Page { get; } = new(permitsPerMinute: 600, burst: 200);

    /// <summary>
    /// Limit for render requests: the preview POST, sent on every debounced keystroke in the
    /// Controls panel. Tighter than <see cref="Page"/> because each request carries client-supplied
    /// props; it still allows several people editing at once from one shared address.
    /// </summary>
    public RazorKitBucketOptions Render { get; } = new(permitsPerMinute: 300, burst: 60);

    /// <summary>
    /// Renders allowed to run at once across all clients; further renders are answered with 503.
    /// Bounds total render work when many clients, or clients that share an address, are active.
    /// </summary>
    public int MaxConcurrentRenders { get; set; } = Math.Max(1, 2 * Environment.ProcessorCount);
}

/// <summary>
/// A token bucket: <see cref="Burst"/> requests can be made at once, and the bucket refills at
/// <see cref="PermitsPerMinute"/>.
/// </summary>
public class RazorKitBucketOptions
{
    public RazorKitBucketOptions(int permitsPerMinute, int burst)
    {
        PermitsPerMinute = permitsPerMinute;
        Burst = burst;
    }

    public int PermitsPerMinute { get; set; }

    public int Burst { get; set; }
}
