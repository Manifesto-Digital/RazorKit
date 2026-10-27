using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Manifesto.RazorKit.Filters;
using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Registry;
using Manifesto.RazorKit.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace Manifesto.RazorKit.Pages;

/// <summary>
/// Resolves the selected component and story for the explorer page. The logic lives here, not
/// in the view, so the page's filters and any later request handling sit on a type RazorKit owns.
/// </summary>
/// <remarks>
/// [IgnoreAntiforgeryToken] stops Razor Pages' automatic validation, and any host-global filter,
/// from calling the host's antiforgery service. The page only answers GET and HEAD (the method
/// check); request protection is here as well so every RazorKit path that accepts other methods carries it.
/// </remarks>
[IgnoreAntiforgeryToken]
[ResponseHeaders]
[MethodCheck]
[RateLimit]
[RequestProtection]
public class RazorKitPageModel : PageModel
{
    private readonly ComponentDiscoveryService _componentDiscovery;
    private readonly ComponentPropertyService _propertyService;
    private readonly ComponentStoryService _storyService;
    private readonly RazorKitRegistry _registry;

    // Resolved per request rather than injected: the set is internal, and the constructor is public.
    private RazorKitThemeSet ThemeSet => HttpContext.RequestServices.GetRequiredService<RazorKitThemeSet>();

    public RazorKitPageModel(
        ComponentDiscoveryService componentDiscovery,
        ComponentPropertyService propertyService,
        ComponentStoryService storyService,
        RazorKitRegistry registry)
    {
        _componentDiscovery = componentDiscovery;
        _propertyService = propertyService;
        _storyService = storyService;
        _registry = registry;
    }

    public List<ComponentInfo> Components { get; private set; } = new();

    public ComponentInfo? SelectedComponent { get; private set; }

    public ComponentStory? SelectedStory { get; private set; }

    public ComponentPropertyInfo[] ComponentProperties { get; private set; } = Array.Empty<ComponentPropertyInfo>();

    public Dictionary<string, object> DefaultValues { get; private set; } = new();

    public string InitialIframeUrl { get; private set; } = string.Empty;

    /// <summary>The configured theme names, in order, for the theme dropdown.</summary>
    public IEnumerable<string> ThemeNames => ThemeSet.Themes.Select(t => t.Name);

    /// <summary>True when there are two or more themes to choose between, so the dropdown is shown.</summary>
    public bool ShowThemeSelector => ThemeSet.HasChoice;

    /// <summary>The theme this page shows: the requested one when it is configured, else the default.</summary>
    public string? SelectedThemeName { get; private set; }

    // Whether the URL named a configured theme. When it did not, the page script may still apply
    // the theme the browser remembers.
    private bool ThemeWasRequested { get; set; }

    /// <summary>
    /// The query string RazorKit's own links carry so the theme survives navigation and shared
    /// links: "?theme=name" when there is a choice of themes, otherwise empty. Built from the
    /// configured name, never from the request.
    /// </summary>
    public string ThemeQuery => ShowThemeSelector && SelectedThemeName != null
        ? $"?{RazorKitThemeSet.QueryKey}={Uri.EscapeDataString(SelectedThemeName)}"
        : string.Empty;

    /// <summary>The value the page script posts back with every preview render.</summary>
    public string RequestToken { get; private set; } = string.Empty;

    private Type? PropsType { get; set; }

    // The package version changes with every release, so a new package busts browser caches. The
    // "+<commit sha>" build metadata is stripped so the build commit does not appear in public URLs. IFileVersionProvider cannot be used: it cannot hash the
    // files of a Razor Class Library's static web assets.
    private static readonly string AssetVersion = Uri.EscapeDataString(StripBuildMetadata(
        typeof(RazorKitPageModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(RazorKitPageModel).Assembly.GetName().Version?.ToString()
        ?? "0"));

    internal static string StripBuildMetadata(string version)
    {
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }

    // Our own options rather than the host's MVC JSON options, so a host's naming policy or
    // converters cannot change the shape the script reads. JavaScriptEncoder.Default escapes
    // <, >, & and quotes, so the serialised text can never close the data block's script element.
    private static readonly JsonSerializerOptions PageDataJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Default
    };

    public IActionResult OnGet()
    {
        var requestedTheme = ThemeSet.FindRequested(Request.Query[RazorKitThemeSet.QueryKey]);
        ThemeWasRequested = requestedTheme != null;
        SelectedThemeName = (requestedTheme ?? ThemeSet.Default)?.Name;

        Components = _componentDiscovery.DiscoverComponents();
        var componentName = RouteData.Values["componentName"]?.ToString();
        var storyName = RouteData.Values["storyName"]?.ToString();

        if (string.IsNullOrEmpty(componentName) && Components.Any())
        {
            var firstComponent = Components.First();
            var firstStory = _storyService.GetStoriesForComponent(firstComponent.Name).FirstOrDefault();

            return Redirect(firstStory != null
                ? $"/razorkit/{firstComponent.Name.ToLower()}/{firstStory.Name.ToLower()}{RedirectThemeQuery}"
                : $"/razorkit/{firstComponent.Name.ToLower()}{RedirectThemeQuery}");
        }

        SelectedComponent = Components.FirstOrDefault(c => c.Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));

        if (SelectedComponent != null)
        {
            var stories = _storyService.GetStoriesForComponent(SelectedComponent.Name);

            if (string.IsNullOrEmpty(storyName) && stories.Any())
            {
                return Redirect($"/razorkit/{SelectedComponent.Name.ToLower()}/{stories.First().Name.ToLower()}{RedirectThemeQuery}");
            }

            SelectedStory = stories.FirstOrDefault(s => s.Name.Equals(storyName, StringComparison.OrdinalIgnoreCase));

            PropsType = _registry.FindPropsType(SelectedComponent.Name);
            if (PropsType != null)
            {
                ComponentProperties = _propertyService.GetComponentProperties(PropsType).ToArray();

                if (SelectedStory != null)
                {
                    DefaultValues = SelectedStory.Properties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                    var componentDefaults = _propertyService.GetDefaultValues(PropsType);
                    foreach (var prop in ComponentProperties)
                    {
                        if (!DefaultValues.ContainsKey(prop.Name))
                        {
                            DefaultValues[prop.Name] = componentDefaults.GetValueOrDefault(prop.Name) ?? prop.DefaultValue ?? new object();
                        }
                    }
                }
                else
                {
                    DefaultValues = _propertyService.GetDefaultValues(PropsType);
                }
            }

            InitialIframeUrl = $"/razorkit-preview/preview/{SelectedComponent.Name.ToLower()}/{SelectedStory?.Name.ToLower() ?? "default"}{ThemeQuery}";
        }

        RequestToken = Filters.RequestToken.Issue(HttpContext);
        return Page();
    }

    // A redirect keeps only a theme the URL actually named, so a link without one still lands on a
    // page that can apply the remembered theme.
    private string RedirectThemeQuery => ThemeWasRequested ? ThemeQuery : string.Empty;

    /// <summary>
    /// The reset button's label: what resetting the controls returns them to. A story that sets
    /// props is reset to its own values; otherwise the controls hold the component defaults.
    /// </summary>
    public string ResetLabel => SelectedStory is { Properties.Count: > 0 }
        ? "Reset to story values"
        : "Reset to defaults";

    /// <summary>
    /// Stories for the sidebar. Looked up per component while the view renders, as the view always did.
    /// </summary>
    public List<ComponentStory> GetStories(string componentName) => _storyService.GetStoriesForComponent(componentName);

    /// <summary>
    /// A props instance built from the current defaults, for the code snippet in the Controls panel.
    /// </summary>
    public object CreateDefaultsInstance() => _propertyService.CreateComponentInstance(PropsType!, DefaultValues);

    /// <summary>
    /// The URL of one of the page's own static files, under the host's path base and versioned
    /// with the package, so the files are found when the site is mounted under a sub-path.
    /// </summary>
    public string AssetUrl(string relativePath) =>
        $"{Request.PathBase}/_content/Manifesto.RazorKit/{relativePath}?v={AssetVersion}";

    /// <summary>The JSON for the page's data block: the server values the page script reads.</summary>
    public string PageDataJson() => SerializePageData(new RazorKitPageData(
        Filters.RequestToken.FormField,
        SelectedComponent?.Name,
        SelectedComponent?.Path,
        ComponentProperties.ToDictionary(
            p => p.Name,
            p => new RazorKitPropertyData(p.IsCollection, p.IsComplexType, p.Type.Name)))
    {
        // Empty unless there is a choice of themes: with one theme or none the script has nothing to do.
        Themes = ShowThemeSelector ? ThemeNames.ToArray() : Array.Empty<string>(),
        Theme = ShowThemeSelector ? SelectedThemeName : null,
        ThemeRequested = ShowThemeSelector && ThemeWasRequested
    });

    internal static string SerializePageData(RazorKitPageData data) => JsonSerializer.Serialize(data, PageDataJsonOptions);
}

/// <summary>The server data the explorer page script reads from the page's JSON data block.</summary>
internal sealed record RazorKitPageData(
    [property: JsonPropertyName("tokenField")] string TokenField,
    [property: JsonPropertyName("componentName")] string? ComponentName,
    [property: JsonPropertyName("componentPath")] string? ComponentPath,
    [property: JsonPropertyName("properties")] Dictionary<string, RazorKitPropertyData> Properties)
{
    /// <summary>The theme names the script accepts from the dropdown or localStorage.</summary>
    [JsonPropertyName("themes")]
    public string[] Themes { get; init; } = Array.Empty<string>();

    /// <summary>The theme the server rendered this page and its preview with.</summary>
    [JsonPropertyName("theme")]
    public string? Theme { get; init; }

    /// <summary>Whether that theme came from the URL; when not, a remembered theme may replace it.</summary>
    [JsonPropertyName("themeRequested")]
    public bool ThemeRequested { get; init; }
}

internal sealed record RazorKitPropertyData(
    [property: JsonPropertyName("isCollection")] bool IsCollection,
    [property: JsonPropertyName("isComplexType")] bool IsComplexType,
    [property: JsonPropertyName("typeName")] string TypeName);
