using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Manifesto.RazorKit.Tests.Support;
using Microsoft.Extensions.Logging;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// The Controls re-render (the protected POST, also used by a theme switch) must rebuild props
/// typed as an interface or <c>object</c> as the runtime types the story used, because the
/// component's partials declare those concrete types. Found in the 0.3.0 consumer smoke test,
/// where the GET preview of such stories worked and every POST re-render of them failed.
/// </summary>
public partial class InterfacePropsRerenderTests
{
    private const string PropertyServiceCategory = "Manifesto.RazorKit.Services.ComponentPropertyService";
    private const string ControllerCategory = "Manifesto.RazorKit.Controllers.RazorKitController";

    [GeneratedRegex("<textarea[^>]*\\bname=\"([^\"]+)\"[^>]*>(.*?)</textarea>", RegexOptions.Singleline)]
    private static partial Regex Textarea();

    [GeneratedRegex("<input[^>]*\\btype=\"(text|checkbox)\"[^>]*>", RegexOptions.Singleline)]
    private static partial Regex Input();

    [GeneratedRegex("\\bname=\"([^\"]+)\"")]
    private static partial Regex NameAttribute();

    [GeneratedRegex("\\bvalue=\"([^\"]*)\"")]
    private static partial Regex ValueAttribute();

    [GeneratedRegex("<meta name=\"razorkit-token\" content=\"([^\"]*)\"")]
    private static partial Regex MetaToken();

    private sealed record LoadedStory(string CookieValue, string PageValue, JsonObject Controls);

    /// <summary>
    /// Loads the explorer page for a story and reads its Controls the way the page script does
    /// (text inputs as strings, checkboxes as booleans, textareas parsed as JSON), so the POST
    /// carries exactly what a browser sends when nothing was edited.
    /// </summary>
    private static async Task<LoadedStory> LoadStoryAsync(HttpClient client, string component, string story)
    {
        var response = await client.GetAsync($"/razorkit/{component}/{Uri.EscapeDataString(story)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith(CookieName + "=", StringComparison.Ordinal));
        var cookieValue = setCookie[(CookieName.Length + 1)..].Split(';')[0];
        var pageValue = WebUtility.HtmlDecode(MetaToken().Match(html).Groups[1].Value);

        var formStart = html.IndexOf("<form id=\"component-form\"", StringComparison.Ordinal);
        Assert.True(formStart >= 0, "The page did not render the Controls form.");
        var form = html[formStart..html.IndexOf("</form>", formStart, StringComparison.Ordinal)];

        var controls = new JsonObject();
        foreach (Match input in Input().Matches(form))
        {
            var name = NameAttribute().Match(input.Value).Groups[1].Value;
            controls[name] = input.Groups[1].Value == "checkbox"
                ? JsonValue.Create(input.Value.Contains(" checked", StringComparison.Ordinal))
                : JsonValue.Create(WebUtility.HtmlDecode(ValueAttribute().Match(input.Value).Groups[1].Value));
        }

        foreach (Match textarea in Textarea().Matches(form))
        {
            var text = WebUtility.HtmlDecode(textarea.Groups[2].Value).Trim();
            controls[textarea.Groups[1].Value] = text.Length == 0 ? null : JsonNode.Parse(text);
        }

        return new LoadedStory(cookieValue, pageValue, controls);
    }

    private static async Task<string> PostAsync(HttpClient client, LoadedStory loaded, string component, string story, JsonObject props)
    {
        var path = $"/razorkit-preview/preview/{component}/{Uri.EscapeDataString(story)}";
        var response = await client.SendAsync(RenderPost(loaded.CookieValue, loaded.PageValue, props.ToJsonString(), path: path));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> GetPreviewAsync(HttpClient client, string component, string story)
    {
        var response = await client.GetAsync($"/razorkit-preview/preview/{component}/{Uri.EscapeDataString(story)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static void AssertRendered(string html)
    {
        Assert.DoesNotContain("could not be rendered", html);
        Assert.DoesNotContain("was not found", html);
    }

    [Theory]
    [InlineData("banner", "Default")]
    [InlineData("banner", "No Picture")]
    [InlineData("banner", "Title Only")]
    [InlineData("slider", "Basic")]
    [InlineData("gallery", "Default")]
    public async Task Unedited_controls_posted_back_render_the_same_as_the_story(string component, string story)
    {
        await using var host = await TestHost.StartAsync();
        var expected = await GetPreviewAsync(host.Client, component, story);
        AssertRendered(expected);

        var loaded = await LoadStoryAsync(host.Client, component, story);
        var posted = await PostAsync(host.Client, loaded, component, story, loaded.Controls);

        AssertRendered(posted);
        Assert.Equal(expected, posted);
    }

    [Fact]
    public async Task An_edit_inside_an_interface_typed_prop_keeps_the_storys_runtime_type()
    {
        await using var host = await TestHost.StartAsync();
        var loaded = await LoadStoryAsync(host.Client, "banner", "Default");
        loaded.Controls["Action"]!["Text"] = "Edited action";
        loaded.Controls["Title"] = "Edited title";

        var html = await PostAsync(host.Client, loaded, "banner", "Default", loaded.Controls);

        AssertRendered(html);
        Assert.Contains("<a class=\"action action--secondary\" href=\"/story\">Edited action</a>", html);
        Assert.Contains("<h1>Edited title</h1>", html);
        Assert.Contains("data-picture=\"/story.png\"", html);
    }

    [Fact]
    public async Task An_item_added_to_an_object_list_takes_the_type_the_storys_items_share()
    {
        await using var host = await TestHost.StartAsync();
        var loaded = await LoadStoryAsync(host.Client, "slider", "Basic");
        var items = loaded.Controls["Items"]!.AsArray();
        items.Add(new JsonObject { ["Title"] = "Slide three" });
        loaded.Controls["ShowArrows"] = false;

        var html = await PostAsync(host.Client, loaded, "slider", "Basic", loaded.Controls);

        AssertRendered(html);
        Assert.Contains("<h3>Slide three</h3>", html);
        Assert.Contains("href=\"/one\">One</a>", html);
        Assert.Contains("data-arrows=\"false\"", html);
    }

    [Fact]
    public async Task An_edit_nested_in_a_list_of_an_interface_type_renders_through_the_nested_partial()
    {
        await using var host = await TestHost.StartAsync();
        var loaded = await LoadStoryAsync(host.Client, "gallery", "Default");
        loaded.Controls["Tiles"]![1]!["Link"]!["Text"] = "Edited two";

        var html = await PostAsync(host.Client, loaded, "gallery", "Default", loaded.Controls);

        AssertRendered(html);
        Assert.Contains("<a class=\"action action--secondary\" href=\"/one\">Open one</a>", html);
        Assert.Contains("<a class=\"action action--primary\" href=\"/two\">Edited two</a>", html);
    }

    [Fact]
    public async Task A_type_named_in_the_posted_json_is_ignored()
    {
        await using var host = await TestHost.StartAsync();
        var loaded = await LoadStoryAsync(host.Client, "banner", "Default");
        var props = JsonNode.Parse("""
            {
              "Title": "Banner title",
              "Action": {
                "$type": "Manifesto.RazorKit.Tests.Components.Shared.LinkProps, Manifesto.RazorKit.Tests",
                "$Type": "System.IO.FileInfo",
                "Text": "Typed in request",
                "Url": "/x"
              }
            }
            """)!.AsObject();

        var html = await PostAsync(host.Client, loaded, "banner", "Default", props);

        AssertRendered(html);
        Assert.Contains("<a class=\"action action--primary\" href=\"/x\">Typed in request</a>", html);
    }

    [Fact]
    public async Task A_value_that_cannot_be_rebuilt_keeps_the_storys_value_and_logs_only_the_property_and_exception_type()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await TestHost.StartAsync(new TestHostOptions { LoggerProvider = logs });
        var loaded = await LoadStoryAsync(host.Client, "slider", "Basic");
        loaded.Controls["Items"] = JsonNode.Parse("[{\"Title\":[\"secret-prop-value\"]}]");
        loaded.Controls["ShowArrows"] = false;

        var html = await PostAsync(host.Client, loaded, "slider", "Basic", loaded.Controls);

        AssertRendered(html);
        Assert.Contains("<h3>Slide one</h3>", html);
        Assert.Contains("<h3>Slide two</h3>", html);
        Assert.Contains("data-arrows=\"false\"", html);
        Assert.Contains(logs.Entries, e =>
            e.Category == ControllerCategory
            && e.State.Contains("PropertyName=Items")
            && e.State.Contains("ExceptionType=JsonException"));
        var razorKitEntries = logs.Entries.Where(e => e.Category.StartsWith("Manifesto.RazorKit", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(razorKitEntries, e => e.Message.Contains("secret-prop-value") || e.State.Contains("secret-prop-value"));
        Assert.DoesNotContain(logs.Entries, e => e.Category == PropertyServiceCategory && e.Level >= LogLevel.Warning);
    }
}
