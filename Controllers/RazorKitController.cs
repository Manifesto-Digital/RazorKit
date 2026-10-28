using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Manifesto.RazorKit.Converters;
using Manifesto.RazorKit.Filters;
using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Registry;
using Manifesto.RazorKit.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Manifesto.RazorKit.Controllers;

// No [ApiController]: its automatic 400 responses are JSON problem details, wrong for an HTML
// endpoint. [IgnoreAntiforgeryToken] keeps a host's global antiforgery filter off RazorKit;
// RazorKit's own token is checked by the request-protection filter instead.
[Route("razorkit-preview")]
[IgnoreAntiforgeryToken]
[ResponseHeaders]
[RateLimit]
[RequestProtection]
[ConcurrencyLimit]
public partial class RazorKitController : Controller
{
    // Props come from a person editing the Controls panel; nothing legitimate nests deeper.
    private static readonly JsonDocumentOptions PropsJsonDocumentOptions = new() { MaxDepth = 16 };

    private readonly ComponentDiscoveryService _componentDiscovery;
    private readonly ComponentPropertyService _propertyService;
    private readonly ComponentStoryService _storyService;
    private readonly ICompositeViewEngine _viewEngine;
    private readonly RazorKitOptions _options;
    private readonly RazorKitRegistry _registry;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<RazorKitController> _logger;

    // Resolved per request rather than injected: the set is internal, and the constructor is public.
    private RazorKitThemeSet ThemeSet => HttpContext.RequestServices.GetRequiredService<RazorKitThemeSet>();

    public RazorKitController(
        ComponentDiscoveryService componentDiscovery,
        ComponentPropertyService propertyService,
        ComponentStoryService storyService,
        ICompositeViewEngine viewEngine,
        RazorKitOptions options,
        RazorKitRegistry registry,
        IHostEnvironment environment,
        ILogger<RazorKitController> logger)
    {
        _componentDiscovery = componentDiscovery;
        _propertyService = propertyService;
        _storyService = storyService;
        _viewEngine = viewEngine;
        _options = options;
        _registry = registry;
        _environment = environment;
        _logger = logger;
    }

    // GET takes no props from any source, so only a POST that passed request protection can
    // change what the preview renders. The iframe of a component without controls loads this GET; "Open in
    // new window" is a form POST.
    [HttpGet("preview/{componentName}/{storyName?}")]
    public Task<IActionResult> Preview([FromRoute] string componentName, [FromRoute] string storyName = "default") =>
        RenderPreviewAsync(componentName, storyName, propsJson: null);

    [HttpPost("preview/{componentName}/{storyName?}")]
    public Task<IActionResult> PreviewWithProps(
        [FromRoute] string componentName,
        [FromRoute] string storyName = "default",
        [FromForm] string? propsJson = null) =>
        RenderPreviewAsync(componentName, storyName, propsJson);

    private async Task<IActionResult> RenderPreviewAsync(string componentName, string storyName, string? propsJson)
    {
        List<ComponentInfo> components = _componentDiscovery.DiscoverComponents();
        ComponentInfo? component = components.FirstOrDefault(c =>
            c.Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));

        if (component == null)
        {
            return ErrorContent("Component not found.", detail: null);
        }

        Type? propsType = _registry.FindPropsType(componentName);

        if (propsType == null)
        {
            return ErrorContent("Props type not found.", detail: null);
        }

        try
        {
            Dictionary<string, object> propertyValues;
            ComponentStory? selectedStory = _storyService.GetStoriesForComponent(componentName)
                .FirstOrDefault(s => s.Name.Equals(storyName, StringComparison.OrdinalIgnoreCase));

            // Check if this is a POST with JSON props
            if (!string.IsNullOrEmpty(propsJson))
            {
                try
                {
                    // Deserialize the JSON props
                    using var jsonDocument = JsonDocument.Parse(propsJson, PropsJsonDocumentOptions);
                    propertyValues = DeserializePropsFromJson(jsonDocument.RootElement, propsType, selectedStory?.Properties);
                }
                catch (JsonException jsonEx)
                {
                    LogInvalidProps(_logger, componentName, jsonEx.GetType().Name);
                    return ErrorContent("The props are not valid JSON.", jsonEx.Message);
                }
            }
            else
            {
                // Fall back to story defaults or default values
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
            LogRenderFailed(_logger, componentName, ex);
            return ErrorContent("The component could not be rendered.", ex.Message);
        }
    }

    // storyValues are the selected story's own values. They are the only source of runtime types for
    // members declared as an interface, object or base class, and the fallback when a posted value
    // for such a member cannot be rebuilt.
    private Dictionary<string, object> DeserializePropsFromJson(JsonElement jsonElement, Type propsType, Dictionary<string, object>? storyValues)
    {
        var propertyValues = new Dictionary<string, object>();

        // Create JsonSerializerOptions with InterfaceToConcreteConverter
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new InterfaceToConcreteConverter(_registry) }
        };
        var storyGuidedReader = new StoryGuidedPropsReader(jsonOptions);

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
                            else if (propInfo.PropertyType.IsAbstract && !propInfo.PropertyType.IsAssignableFrom(typeof(string))
                                     && storyValues?.GetValueOrDefault(propInfo.Name) is { } guidedStoryValue)
                            {
                                // A string can never be the interface or base class the story's value stands in for;
                                // handing it to the partial fails the whole render, so keep the story's value.
                                LogPropKeptStoryValue(_logger, propInfo.Name, nameof(NotSupportedException));
                                value = guidedStoryValue;
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
                            object? storyValue = storyValues?.GetValueOrDefault(propInfo.Name);
                            try
                            {
                                value = storyGuidedReader.Read(property.Value, propInfo.PropertyType, storyValue);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                if (storyValue == null)
                                {
                                    LogPropDeserializeFailed(_logger, property.Name, ex.GetType().Name);
                                    // Continue with other properties
                                    continue;
                                }

                                // One unreadable member must not cost the whole render: the story's value is still a
                                // value the component accepts.
                                LogPropKeptStoryValue(_logger, propInfo.Name, ex.GetType().Name);
                                value = storyValue;
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
            LogPropsDeserializeFailed(_logger, ex.GetType().Name);
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
                LogViewNotFound(_logger, viewPath);
                return ErrorHtml("The component view was not found.", viewPath);
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
            LogViewFailed(_logger, viewPath, ex);
            return ErrorHtml("The component view could not be rendered.", ex.Message);
        }
    }

    // Every error the preview renders goes through here. Outside Development the response carries
    // only the fixed message: exception text can quote request data or server internals. In
    // Development the detail is shown HTML-encoded. Stack traces only ever go to the log.
    private string ErrorHtml(string message, string? detail)
    {
        var html = $"<div style='padding: 2rem; color: red;'><p><strong>{HtmlEncoder.Default.Encode(message)}</strong></p>";
        if (detail != null)
        {
            html += _environment.IsDevelopment()
                ? $"<p>{HtmlEncoder.Default.Encode(detail)}</p>"
                : "<p>Check the application log for details.</p>";
        }

        return html + "</div>";
    }

    private ContentResult ErrorContent(string message, string? detail) =>
        Content(CreatePreviewHtml(ErrorHtml(message, detail)), "text/html");

    [LoggerMessage(EventId = 20, Level = LogLevel.Warning, Message = "Preview props for component {ComponentName} were not valid JSON ({ExceptionType}).")]
    private static partial void LogInvalidProps(ILogger logger, string componentName, string exceptionType);

    [LoggerMessage(EventId = 21, Level = LogLevel.Error, Message = "Rendering the preview for component {ComponentName} failed.")]
    private static partial void LogRenderFailed(ILogger logger, string componentName, Exception exception);

    [LoggerMessage(EventId = 22, Level = LogLevel.Error, Message = "Rendering the view {ViewPath} failed.")]
    private static partial void LogViewFailed(ILogger logger, string viewPath, Exception exception);

    [LoggerMessage(EventId = 23, Level = LogLevel.Warning, Message = "The view {ViewPath} was not found.")]
    private static partial void LogViewNotFound(ILogger logger, string viewPath);

    [LoggerMessage(EventId = 24, Level = LogLevel.Debug, Message = "Property {PropertyName} could not be deserialized and was skipped ({ExceptionType}).")]
    private static partial void LogPropDeserializeFailed(ILogger logger, string propertyName, string exceptionType);

    [LoggerMessage(EventId = 26, Level = LogLevel.Debug, Message = "Property {PropertyName} could not be rebuilt from the posted value, so the story's value was used ({ExceptionType}).")]
    private static partial void LogPropKeptStoryValue(ILogger logger, string propertyName, string exceptionType);

    [LoggerMessage(EventId = 25, Level = LogLevel.Warning, Message = "Preview props could not be fully deserialized ({ExceptionType}).")]
    private static partial void LogPropsDeserializeFailed(ILogger logger, string exceptionType);

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
        // PathBase keeps the bundled script reachable when the host is mounted under a sub-path.
        var axePath = HtmlEncoder.Default.Encode($"{Request.PathBase}/_content/Manifesto.RazorKit/lib/axe-core/axe.min.js");
        var jsScript = !string.IsNullOrEmpty(jsPath)
            ? $"<script src=\"{jsPath}\"></script>"
            : string.Empty;

        // The theme is set here, in the served document, so the component's first paint is already
        // themed and nothing has to reach into the frame. The request only selects a configured
        // entry; the attribute value is always that entry's validated DataTheme.
        var theme = ThemeSet.Resolve(Request.Query[RazorKitThemeSet.QueryKey]);
        var themeAttribute = theme != null
            ? $" data-theme=\"{HtmlEncoder.Default.Encode(theme.DataTheme)}\""
            : string.Empty;

        return $@"<!DOCTYPE html>
<html lang=""en""{themeAttribute}>
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Component Preview</title>
    {cssLink}
    <script src=""{axePath}""></script>
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
