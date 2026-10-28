namespace Manifesto.RazorKit.Models;

/// <summary>
/// A theme the preview can be rendered in. The preview document's <c>&lt;html&gt;</c> element gets
/// <c>data-theme="<see cref="DataTheme"/>"</c>, which the component library's stylesheet keys off.
/// </summary>
public class RazorKitTheme
{
    /// <summary>
    /// The name shown in the theme dropdown and carried as <c>?theme=</c> on RazorKit's links.
    /// Must be unique, ignoring case.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The value written to the preview's <c>data-theme</c> attribute. Letters, digits, hyphens
    /// and underscores only.
    /// </summary>
    public string DataTheme { get; set; } = string.Empty;
}
