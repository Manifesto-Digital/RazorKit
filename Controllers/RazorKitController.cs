using System.Reflection;
using System.Text.Json;
using Manifesto.RazorKit.Constants;
using Manifesto.RazorKit.Helpers;
using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Manifesto.RazorKit.Controllers;

[ApiController]
[Route(RazorKitConstants.Routes.PreviewBase)]
public class RazorKitController : Controller
{
    private readonly ComponentDiscoveryService _componentDiscovery;
    private readonly ComponentPropertyService _propertyService;
    private readonly ComponentStoryService _storyService;
    private readonly ICompositeViewEngine _viewEngine;
    private readonly RazorKitOptions _options;
    private readonly PreviewHtmlBuilder _htmlBuilder;

    public RazorKitController(
        ComponentDiscoveryService componentDiscovery,
        ComponentPropertyService propertyService,
        ComponentStoryService storyService,
        ICompositeViewEngine viewEngine,
        RazorKitOptions options)
    {
        _componentDiscovery = componentDiscovery;
        _propertyService = propertyService;
        _storyService = storyService;
        _viewEngine = viewEngine;
        _options = options;
        _htmlBuilder = new PreviewHtmlBuilder(options);
    }

    [HttpGet("preview/{componentName}/{storyName?}")]
    [HttpPost("preview/{componentName}/{storyName?}")]
    public async Task<IActionResult> Preview(string componentName, string storyName = RazorKitConstants.Defaults.StoryName, [FromForm] string? propsJson = null)
    {
        List<ComponentInfo> components = _componentDiscovery.DiscoverComponents();
        ComponentInfo? component = components.FirstOrDefault(c =>
            c.Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));

        if (component == null)
        {
            return Content(
                _htmlBuilder.Build($"<div style='padding: 2rem;'>{RazorKitConstants.ErrorMessages.ComponentNotFound}</div>"),
                "text/html"
            );
        }

        Type? propsType = FindPropsType(componentName);

        if (propsType == null)
        {
            return Content(
                _htmlBuilder.Build($"<div style='padding: 2rem;'>{RazorKitConstants.ErrorMessages.PropsTypeNotFound}</div>"),
                "text/html"
            );
        }

        try
        {
            Dictionary<string, object> propertyValues = GetPropertyValues(propsJson, componentName, storyName, propsType);

            var componentInstance = _propertyService.CreateComponentInstance(propsType, propertyValues);
            var componentHtml = await RenderComponentAsync(component.Path, componentInstance);
            var fullHtml = _htmlBuilder.Build(componentHtml);

            return Content(fullHtml, "text/html");
        }
        catch (Exception ex)
        {
            var errorHtml = _htmlBuilder.Build($@"<div style='padding: 2rem; color: red;'>
                <p><strong>Error rendering component:</strong></p>
                <p>{ex.Message}</p>
                <pre style='font-size: 0.75rem; margin-top: 1rem; overflow: auto;'>{ex.StackTrace}</pre>
            </div>");
            return Content(errorHtml, "text/html");
        }
    }

    /// <summary>
    /// Gets property values from JSON, story, or defaults
    /// </summary>
    private Dictionary<string, object> GetPropertyValues(string? propsJson, string componentName, string storyName, Type propsType)
    {
        // Check if this is a POST with JSON props
        if (!string.IsNullOrEmpty(propsJson))
        {
            try
            {
                using var jsonDocument = JsonDocument.Parse(propsJson);
                return PropertyDeserializer.DeserializePropsFromJson(jsonDocument.RootElement, propsType);
            }
            catch (JsonException jsonEx)
            {
                throw new InvalidOperationException($"{RazorKitConstants.ErrorMessages.InvalidJson}: {jsonEx.Message}", jsonEx);
            }
        }

        // Fall back to story defaults or default values
        List<ComponentStory> stories = _storyService.GetStoriesForComponent(componentName);
        ComponentStory? selectedStory = stories.FirstOrDefault(s => 
            s.Name.Equals(storyName, StringComparison.OrdinalIgnoreCase));

        if (selectedStory != null && selectedStory.Properties.Any())
        {
            return selectedStory.Properties;
        }

        return _propertyService.GetDefaultValues(propsType);
    }

    /// <summary>
    /// Finds the Props type for a given component name
    /// </summary>
    private Type? FindPropsType(string componentName)
    {
        IEnumerable<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));

        foreach (Assembly assembly in assemblies)
        {
            try
            {
                var propsTypeName = $"{componentName}{RazorKitConstants.ComponentNaming.PropsSuffix}";
                var propsType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name.Equals(propsTypeName, StringComparison.OrdinalIgnoreCase));
                
                if (propsType != null)
                {
                    return propsType;
                }
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    private async Task<string> RenderComponentAsync(string viewPath, object model)
    {
        try
        {
            ViewData.Model = model;
            using var writer = new StringWriter();

            ViewEngineResult viewResult = _viewEngine.GetView(null, viewPath, false);

            if (viewResult.View == null)
            {
                var cleanPath = viewPath.TrimStart('~', '/');
                viewResult = _viewEngine.GetView(null, cleanPath, false);
            }

            if (viewResult.View == null)
            {
                var fileName = Path.GetFileName(viewPath);
                viewResult = _viewEngine.GetView(null, fileName, false);
            }

            if (viewResult.View == null)
            {
                return $@"<div style='padding: 2rem; color: red;'>
                    <p><strong>View not found:</strong> {viewPath}</p>
                </div>";
            }

            var viewContext = new ViewContext(
                ControllerContext,
                viewResult.View,
                ViewData,
                TempData,
                writer,
                new HtmlHelperOptions()
            );

            await viewResult.View.RenderAsync(viewContext);
            return writer.GetStringBuilder().ToString();
        }
        catch (Exception ex)
        {
            return $@"<div style='padding: 2rem; color: red;'>
                <p><strong>Error rendering view:</strong></p>
                <p>{ex.Message}</p>
            </div>";
        }
    }
}
