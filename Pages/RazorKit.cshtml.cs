using Manifesto.RazorKit.Filters;
using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Manifesto.RazorKit.Pages;

/// <summary>
/// Resolves the selected component and story for the explorer page. The logic lives here, not
/// in the view, so the page's filters and any later request handling sit on a type RazorKit owns.
/// </summary>
[ResponseHeaders]
public class RazorKitPageModel : PageModel
{
    private readonly ComponentDiscoveryService _componentDiscovery;
    private readonly ComponentPropertyService _propertyService;
    private readonly ComponentStoryService _storyService;

    public RazorKitPageModel(
        ComponentDiscoveryService componentDiscovery,
        ComponentPropertyService propertyService,
        ComponentStoryService storyService)
    {
        _componentDiscovery = componentDiscovery;
        _propertyService = propertyService;
        _storyService = storyService;
    }

    public List<ComponentInfo> Components { get; private set; } = new();

    public ComponentInfo? SelectedComponent { get; private set; }

    public ComponentStory? SelectedStory { get; private set; }

    public ComponentPropertyInfo[] ComponentProperties { get; private set; } = Array.Empty<ComponentPropertyInfo>();

    public Dictionary<string, object> DefaultValues { get; private set; } = new();

    public string InitialIframeUrl { get; private set; } = string.Empty;

    private Type? PropsType { get; set; }

    public IActionResult OnGet()
    {
        Components = _componentDiscovery.DiscoverComponents();
        var componentName = RouteData.Values["componentName"]?.ToString();
        var storyName = RouteData.Values["storyName"]?.ToString();

        if (string.IsNullOrEmpty(componentName) && Components.Any())
        {
            var firstComponent = Components.First();
            var firstStory = _storyService.GetStoriesForComponent(firstComponent.Name).FirstOrDefault();

            return Redirect(firstStory != null
                ? $"/razorkit/{firstComponent.Name.ToLower()}/{firstStory.Name.ToLower()}"
                : $"/razorkit/{firstComponent.Name.ToLower()}");
        }

        SelectedComponent = Components.FirstOrDefault(c => c.Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));

        if (SelectedComponent != null)
        {
            var stories = _storyService.GetStoriesForComponent(SelectedComponent.Name);

            if (string.IsNullOrEmpty(storyName) && stories.Any())
            {
                return Redirect($"/razorkit/{SelectedComponent.Name.ToLower()}/{stories.First().Name.ToLower()}");
            }

            SelectedStory = stories.FirstOrDefault(s => s.Name.Equals(storyName, StringComparison.OrdinalIgnoreCase));

            PropsType = FindPropsType(SelectedComponent.Name);
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

            InitialIframeUrl = $"/razorkit-preview/preview/{SelectedComponent.Name.ToLower()}/{SelectedStory?.Name.ToLower() ?? "default"}";
        }

        return Page();
    }

    /// <summary>
    /// Stories for the sidebar. Looked up per component while the view renders, as the view always did.
    /// </summary>
    public List<ComponentStory> GetStories(string componentName) => _storyService.GetStoriesForComponent(componentName);

    /// <summary>
    /// A props instance built from the current defaults, for the code snippet in the Controls panel.
    /// </summary>
    public object CreateDefaultsInstance() => _propertyService.CreateComponentInstance(PropsType!, DefaultValues);

    private static Type? FindPropsType(string componentName)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));

        foreach (var assembly in assemblies)
        {
            try
            {
                var propsType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name.Equals($"{componentName}Props", StringComparison.OrdinalIgnoreCase));

                if (propsType != null)
                {
                    return propsType;
                }
            }
            catch (Exception)
            {
                continue;
            }
        }

        return null;
    }
}
