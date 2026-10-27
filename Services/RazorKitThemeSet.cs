using System.Text.RegularExpressions;
using Manifesto.RazorKit.Models;
using Microsoft.Extensions.Primitives;

namespace Manifesto.RazorKit.Services;

/// <summary>A configured theme, copied at startup so later changes to the options list have no effect.</summary>
internal sealed record ThemeEntry(string Name, string DataTheme);

/// <summary>
/// The configured themes, validated once at startup, and the one rule for turning a requested
/// name into a theme. A requested name is only ever used to pick a configured entry; what the
/// preview and the page write out always comes from the entry, never from the request.
/// </summary>
internal sealed partial class RazorKitThemeSet
{
    /// <summary>The query string parameter every RazorKit link, preview GET and render POST carries.</summary>
    public const string QueryKey = "theme";

    // Names appear in URLs, the dropdown and localStorage; nothing needs them longer than this.
    private const int MaxLength = 64;

    private RazorKitThemeSet(ThemeEntry[] themes)
    {
        Themes = themes;
    }

    public IReadOnlyList<ThemeEntry> Themes { get; }

    /// <summary>The first configured theme, or null when there are none.</summary>
    public ThemeEntry? Default => Themes.Count > 0 ? Themes[0] : null;

    /// <summary>A choice only exists with two or more themes; one theme is applied without asking.</summary>
    public bool HasChoice => Themes.Count > 1;

    // data-theme ends up in an attribute in the preview document and in the site's CSS attribute
    // selectors, so the value is restricted to characters that are inert in both. \\z, not $, because $ also matches before a
    // trailing newline.
    [GeneratedRegex("^[A-Za-z0-9_-]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeDataTheme();

    /// <summary>Validates the configured themes, throwing for the first entry that is unusable.</summary>
    public static RazorKitThemeSet Create(IEnumerable<RazorKitTheme?>? configured)
    {
        var themes = new List<ThemeEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var theme in configured ?? Array.Empty<RazorKitTheme>())
        {
            if (theme is null)
            {
                throw Invalid(index, "is null.");
            }

            var name = theme.Name;
            if (string.IsNullOrWhiteSpace(name) || name.Length > MaxLength || name.Trim() != name || name.Any(char.IsControl))
            {
                throw Invalid(index, $"needs a Name of 1 to {MaxLength} characters, with no leading or trailing spaces or control characters.");
            }

            // Unique ignoring case: two names that differ only in case would look the same in the
            // dropdown, and only one of them could ever be matched by a hand-typed link.
            if (!names.Add(name))
            {
                throw Invalid(index, $"has the Name '{name}', which another theme already uses.");
            }

            var dataTheme = theme.DataTheme;
            if (string.IsNullOrEmpty(dataTheme) || dataTheme.Length > MaxLength || !SafeDataTheme().IsMatch(dataTheme))
            {
                throw Invalid(index, $"'{name}' needs a DataTheme of 1 to {MaxLength} letters, digits, hyphens or underscores.");
            }

            themes.Add(new ThemeEntry(name, dataTheme));
            index++;
        }

        return new RazorKitThemeSet(themes.ToArray());
    }

    /// <summary>
    /// The theme a request asked for, when it names exactly one configured theme, case and all.
    /// Anything else (missing, repeated, unknown, a different case) is not a choice and gives null.
    /// </summary>
    public ThemeEntry? FindRequested(StringValues requested)
    {
        if (requested.Count != 1 || requested[0] is not { } name)
        {
            return null;
        }

        foreach (var theme in Themes)
        {
            if (string.Equals(theme.Name, name, StringComparison.Ordinal))
            {
                return theme;
            }
        }

        return null;
    }

    /// <summary>The requested theme when valid, otherwise the default; null only when none are configured.</summary>
    public ThemeEntry? Resolve(StringValues requested) => FindRequested(requested) ?? Default;

    private static ArgumentException Invalid(int index, string problem) =>
        new($"RazorKit Themes entry {index} {problem}", "configure");
}
