using System.Text;
using Manifesto.RazorKit.Models;

namespace Manifesto.RazorKit.Helpers;

/// <summary>
/// Builder for creating preview HTML with proper asset management.
/// Uses timestamp-based cache busting for embedded RCL resources.
/// 
/// Why not use IFileVersionProvider (asp-append-version)?
/// - IFileVersionProvider only works with physical files in wwwroot/
/// - RCL static files (/_content/{Library}/) are embedded resources, not physical files
/// - Embedded resources are compiled into the assembly's DLL at build time
/// - IFileVersionProvider has no way to hash these embedded resources
/// - Result: IFileVersionProvider returns the path unchanged → no cache busting
/// 
/// Our approach:
/// - Generate a fresh timestamp for each preview request
/// - No caching needed - RazorKit is for development/testing only
/// - Always get the latest CSS/JS changes
/// - Extremely lightweight (just DateTime.UtcNow.Ticks)
/// </summary>
public class PreviewHtmlBuilder
{
    private readonly RazorKitOptions _options;

    public PreviewHtmlBuilder(RazorKitOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Gets a timestamp-based version string for asset cache busting.
    /// Generates a fresh timestamp on each call - no caching.
    /// </summary>
    private string GetResourceVersion()
    {
        return DateTime.UtcNow.Ticks.ToString();
    }

    /// <summary>
    /// Builds the complete preview HTML with the component content
    /// </summary>
    public string Build(string componentContent)
    {
        var html = new StringBuilder();
        
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.Append(BuildHead());
        html.Append(BuildBody(componentContent));
        html.AppendLine("</html>");
        
        return html.ToString();
    }

    private string BuildHead()
    {
        var cssLink = BuildCssLink();
        var jsInitScript = BuildInitScript();
        
        return $@"<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Component Preview</title>
    {cssLink}
    {jsInitScript}
    <script src=""https://unpkg.com/axe-core@4.8.2/axe.min.js""></script>
    {BuildInlineStyles()}
</head>
";
    }

    private string BuildCssLink()
    {
        if (string.IsNullOrEmpty(_options.ComponentLibraryName))
            return string.Empty;
            
        var cssPath = $"/_content/{_options.ComponentLibraryName}/css/main.css";
        
        // Note: We can't use IFileVersionProvider here because RCL files are embedded resources,
        // not physical files on disk. IFileVersionProvider would return the path unchanged.
        var version = GetResourceVersion();
        var versionedPath = $"{cssPath}?v={version}";
        
        return $"<link rel=\"stylesheet\" href=\"{versionedPath}\" />";
    }

    private string BuildInitScript()
    {
        return @"<script>
        document.addEventListener('DOMContentLoaded', function () {
            const theme = localStorage.getItem('storybook-theme') || 'charity';
            document.documentElement.setAttribute('data-theme', theme);
        });
    </script>";
    }

    private string BuildInlineStyles()
    {
        return @"<style>
        body {
            margin: 0;
            background: #fff;
            min-height: 100vh;
            display: flex;
            justify-content: center;
        }
        .preview-container {
            width: 100%;
            max-width: none;
        }
        .visually-hidden {
            position: absolute !important;
            width: 1px !important;
            height: 1px !important;
            padding: 0 !important;
            margin: -1px !important;
            overflow: hidden !important;
            clip: rect(0, 0, 0, 0) !important;
            white-space: nowrap !important;
            border: 0 !important;
        }
    </style>";
    }

    private string BuildBody(string componentContent)
    {
        var jsScript = BuildJsScript();
        
        return $@"<body>
    <main class=""preview-container"">
        <h1 class=""visually-hidden"">Component Preview</h1>
        {componentContent}
    </main>
    {jsScript}
</body>
";
    }

    private string BuildJsScript()
    {
        if (string.IsNullOrEmpty(_options.ComponentLibraryName))
            return string.Empty;
            
        var jsPath = $"/_content/{_options.ComponentLibraryName}/js/main.js";
        
        // Note: We can't use IFileVersionProvider here because RCL files are embedded resources,
        // not physical files on disk. IFileVersionProvider would return the path unchanged.
        var version = GetResourceVersion();
        var versionedPath = $"{jsPath}?v={version}";
        
        return $"<script src=\"{versionedPath}\"></script>";
    }
}
