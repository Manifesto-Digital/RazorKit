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
}
