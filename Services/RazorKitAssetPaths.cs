using Manifesto.RazorKit.Helpers;
using Manifesto.RazorKit.Models;

namespace Manifesto.RazorKit.Services;

/// <summary>
/// The host stylesheets and scripts RazorKit loads, validated and copied once at startup, so a
/// later change to the options list has no effect and only a checked path is ever written out.
/// Paths are kept app-relative; each request resolves them against its own path base.
/// </summary>
internal sealed class RazorKitAssetPaths
{
    /// <summary>What the explorer loads when <see cref="RazorKitOptions.ExplorerStylesheets"/> is null: RazorKit's behaviour before it was configurable.</summary>
    internal const string DefaultExplorerStylesheet = "/css/main.css";

    private RazorKitAssetPaths(string[] explorerStylesheets, string[] previewStylesheets, string[] previewScripts)
    {
        ExplorerStylesheets = explorerStylesheets;
        PreviewStylesheets = previewStylesheets;
        PreviewScripts = previewScripts;
    }

    public IReadOnlyList<string> ExplorerStylesheets { get; }

    public IReadOnlyList<string> PreviewStylesheets { get; }

    public IReadOnlyList<string> PreviewScripts { get; }

    /// <summary>Validates the configured lists and fills in the defaults, throwing for the first unusable path.</summary>
    public static RazorKitAssetPaths Create(RazorKitOptions options)
    {
        var explorer = Copy(nameof(RazorKitOptions.ExplorerStylesheets), options.ExplorerStylesheets)
            ?? new[] { DefaultExplorerStylesheet };
        var previewStylesheets = Copy(nameof(RazorKitOptions.PreviewStylesheets), options.PreviewStylesheets)
            ?? Derived(options.ComponentLibraryName, "css/main.css");
        var previewScripts = Copy(nameof(RazorKitOptions.PreviewScripts), options.PreviewScripts)
            ?? Derived(options.ComponentLibraryName, "js/main.js");

        return new RazorKitAssetPaths(explorer, previewStylesheets, previewScripts);
    }

    private static string[]? Copy(string option, IList<string>? configured)
    {
        if (configured is null)
        {
            return null;
        }

        var paths = configured.ToArray();
        for (var index = 0; index < paths.Length; index++)
        {
            if (AppPath.Problem(paths[index]) is { } problem)
            {
                throw new ArgumentException($"RazorKit {option} entry {index} {problem}", "configure");
            }
        }

        return paths;
    }

    // The preview's behaviour before it was configurable: the component library's own static web
    // assets, or nothing when no library is named.
    private static string[] Derived(string? componentLibraryName, string file)
    {
        if (string.IsNullOrEmpty(componentLibraryName))
        {
            return Array.Empty<string>();
        }

        var path = $"/_content/{componentLibraryName}/{file}";
        if (componentLibraryName.IndexOfAny(new[] { '/', '?', '#' }) >= 0 || AppPath.Problem(path) is not null)
        {
            throw new ArgumentException(
                $"RazorKit ComponentLibraryName '{componentLibraryName}' cannot be used in a static file path. " +
                "Use the library's assembly name, or set PreviewStylesheets and PreviewScripts.",
                "configure");
        }

        return new[] { path };
    }
}
