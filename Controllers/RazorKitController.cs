using System.Reflection;
using System.Text.Json;
using Manifesto.RazorKit.Converters;
using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Manifesto.RazorKit.Controllers;

[ApiController]
[Route("razorkit-preview")]
public class RazorKitController : Controller
{
    private readonly ComponentDiscoveryService _componentDiscovery;
    private readonly ComponentPropertyService _propertyService;
    private readonly ComponentStoryService _storyService;
    private readonly ICompositeViewEngine _viewEngine;
    private readonly RazorKitOptions _options;

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
    }

    [HttpGet("preview/{componentName}/{storyName?}")]
    [HttpPost("preview/{componentName}/{storyName?}")]
    public async Task<IActionResult> Preview(string componentName, string storyName = "default", [FromForm] string? propsJson = null)
    {
        List<ComponentInfo> components = _componentDiscovery.DiscoverComponents();
        ComponentInfo? component = components.FirstOrDefault(c =>
            c.Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));

        if (component == null)
        {
            return Content(CreatePreviewHtml("<div style='padding: 2rem;'>Component not found</div>"), "text/html");
        }

        Type? propsType = null;
        IEnumerable<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));

        foreach (Assembly assembly in assemblies)
        {
            try
            {
                propsType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name.Equals($"{componentName}Props", StringComparison.OrdinalIgnoreCase));
                if (propsType != null)
                {
                    break;
                }
            }
            catch
            {
                continue;
            }
        }

        if (propsType == null)
        {
            return Content(CreatePreviewHtml("<div style='padding: 2rem;'>Props type not found</div>"), "text/html");
        }

        try
        {
            Dictionary<string, object> propertyValues;

            // Check if this is a POST with JSON props
            if (!string.IsNullOrEmpty(propsJson))
            {
                try
                {
                    // Deserialize the JSON props
                    using var jsonDocument = JsonDocument.Parse(propsJson);
                    propertyValues = DeserializePropsFromJson(jsonDocument.RootElement, propsType);
                }
                catch (JsonException jsonEx)
                {
                    return Content(CreatePreviewHtml($"<div style='padding: 2rem; color: red;'><p><strong>Invalid JSON:</strong> {jsonEx.Message}</p></div>"), "text/html");
                }
            }
            else
            {
                // Fall back to story defaults or default values
                List<ComponentStory> stories = _storyService.GetStoriesForComponent(componentName);
                ComponentStory? selectedStory = stories.FirstOrDefault(s => s.Name.Equals(storyName, StringComparison.OrdinalIgnoreCase));

                if (selectedStory != null && selectedStory.Properties.Any())
                {
                    propertyValues = selectedStory.Properties;
                }
                else
                {
                    propertyValues = _propertyService.GetDefaultValues(propsType);
                }
            }

            var componentInstance = _propertyService.CreateComponentInstance(propsType, propertyValues);
            var componentHtml = await RenderComponentAsync(component.Path, componentInstance);
            var fullHtml = CreatePreviewHtml(componentHtml);

            return Content(fullHtml, "text/html");
        }
        catch (Exception ex)
        {
            var errorHtml = CreatePreviewHtml($@"<div style='padding: 2rem; color: red;'>
                <p><strong>Error rendering component:</strong></p>
                <p>{ex.Message}</p>
                <pre style='font-size: 0.75rem; margin-top: 1rem; overflow: auto;'>{ex.StackTrace}</pre>
            </div>");
            return Content(errorHtml, "text/html");
        }
    }

    private Dictionary<string, object> DeserializePropsFromJson(JsonElement jsonElement, Type propsType)
    {
        var propertyValues = new Dictionary<string, object>();

        // Create JsonSerializerOptions with InterfaceToConcreteConverter
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new InterfaceToConcreteConverter() }
        };

        try
        {
            // Iterate through JSON properties
            foreach (JsonProperty property in jsonElement.EnumerateObject())
            {
                PropertyInfo? propInfo = propsType.GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (propInfo != null)
                {
                    object? value = null;

                    // Handle different JSON value types
                    switch (property.Value.ValueKind)
                    {
                        case JsonValueKind.String:
                            var stringValue = property.Value.GetString();
                            // Special handling for IHtmlContent - wrap string in HtmlString
                            if (propInfo.PropertyType == typeof(Microsoft.AspNetCore.Html.IHtmlContent))
                            {
                                value = new Microsoft.AspNetCore.Html.HtmlString(stringValue ?? string.Empty);
                            }
                            else
                            {
                                value = stringValue;
                            }
                            break;
                        case JsonValueKind.Number:
                            if (propInfo.PropertyType == typeof(int) || propInfo.PropertyType == typeof(int?))
                            {
                                value = property.Value.GetInt32();
                            }
                            else if (propInfo.PropertyType == typeof(decimal) || propInfo.PropertyType == typeof(decimal?))
                            {
                                value = property.Value.GetDecimal();
                            }
                            else if (propInfo.PropertyType == typeof(double) || propInfo.PropertyType == typeof(double?))
                            {
                                value = property.Value.GetDouble();
                            }
                            else
                            {
                                value = property.Value.GetInt32();
                            }
                            break;
                        case JsonValueKind.True:
                            value = true;
                            break;
                        case JsonValueKind.False:
                            value = false;
                            break;
                        case JsonValueKind.Array:
                        case JsonValueKind.Object:
                            // Deserialize complex types using the converter
                            try
                            {
                                value = JsonSerializer.Deserialize(property.Value.GetRawText(), propInfo.PropertyType, jsonOptions);
                            }
                            catch (JsonException jsonEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error deserializing property {property.Name}: {jsonEx.Message}");
                                // Continue with other properties
                                continue;
                            }
                            break;
                    }

                    if (value != null)
                    {
                        propertyValues[property.Name] = value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deserializing props: {ex.Message}");
        }

        return propertyValues;
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

    private string CreatePreviewHtml(string content)
    {
        var cssPath = !string.IsNullOrEmpty(_options.ComponentLibraryName)
            ? $"/_content/{_options.ComponentLibraryName}/css/main.css"
            : string.Empty;
        var jsPath = !string.IsNullOrEmpty(_options.ComponentLibraryName)
            ? $"/_content/{_options.ComponentLibraryName}/js/main.js"
            : string.Empty;

        var cssLink = !string.IsNullOrEmpty(cssPath)
            ? $"<link rel=\"stylesheet\" href=\"{cssPath}\" />"
            : string.Empty;
        var jsScript = !string.IsNullOrEmpty(jsPath)
            ? $"<script src=\"{jsPath}\"></script>"
            : string.Empty;

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Component Preview</title>
    {cssLink}
    <script>
        document.addEventListener('DOMContentLoaded', function () {{
            const theme = localStorage.getItem('storybook-theme') || 'charity';
            document.documentElement.setAttribute('data-theme', theme);
        }});
    </script>
    <script src=""https://unpkg.com/axe-core@4.8.2/axe.min.js""></script>
    <style>
        body {{
            margin: 0;
            background: #fff;
            min-height: 100vh;
            display: flex;
            justify-content: center;
        }}
        .preview-container {{
            width: 100%;
            max-width: none;
        }}
        .visually-hidden {{
            position: absolute !important;
            width: 1px !important;
            height: 1px !important;
            padding: 0 !important;
            margin: -1px !important;
            overflow: hidden !important;
            clip: rect(0, 0, 0, 0) !important;
            white-space: nowrap !important;
            border: 0 !important;
        }}
    </style>
</head>
<body>
    <main class=""preview-container"">
        <h1 class=""visually-hidden"">Component Preview</h1>
        {content}
    </main>
    {jsScript}
</body>
</html>";
    }
}
