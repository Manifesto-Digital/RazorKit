using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Pages;
using Manifesto.RazorKit.Tests.Support;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// Every preview render that carries the Controls values, including a viewport change and
/// "Open in new window", is the same token-carrying form POST, so edits survive and the props
/// never travel in a URL, storage or window name. The script is checked as served; what the
/// browser does with it (headers, cookie, rendering) was walked through in Playwright.
/// </summary>
public partial class PreviewSubmitTests
{
    [GeneratedRegex("data-razorkit-action=\"reset-form\">\\s*([^<]*?)\\s*</button>")]
    private static partial Regex ResetButton();

    private static string FunctionBody(string script, string name)
    {
        var start = script.IndexOf($"function {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} was not found in the page script.");
        var next = script.IndexOf("\nfunction ", start + 1, StringComparison.Ordinal);
        return next < 0 ? script[start..] : script[start..next];
    }

    [Fact]
    public async Task Open_in_new_window_submits_the_protected_post_to_a_new_tab()
    {
        var script = await PageAssets.LoadScriptAsync();
        var open = FunctionBody(script, "openComponentInNewWindow");

        Assert.Contains("submitPreview('_blank', controls.url, JSON.stringify(controls.props));", open);
        Assert.DoesNotContain("window.open", script);
    }

    [Fact]
    public async Task The_submit_helper_posts_the_props_and_the_token_and_cuts_the_opener()
    {
        var script = await PageAssets.LoadScriptAsync();
        var submit = FunctionBody(script, "submitPreview");

        Assert.Contains("form.method = 'POST';", submit);
        Assert.Contains("input.name = 'propsJson';", submit);
        Assert.Contains("tokenInput.name = razorKitData.tokenField;", submit);
        Assert.Contains("form.setAttribute('rel', 'noopener');", submit);
        Assert.Contains("submitPreview(iframe.name || 'preview-iframe', url, JSON.stringify(data));", FunctionBody(script, "postToIframe"));
    }

    [Fact]
    public async Task Props_never_go_into_a_url_storage_window_name_or_a_message()
    {
        var script = await PageAssets.LoadScriptAsync();

        // The URL is the server-given preview route (under the path base) plus the component and
        // story names, and nothing else; the props travel only in the returned object.
        Assert.Contains("return { url: `${razorKitData.previewUrl}/${componentName}/${storyName}`, props: props };", FunctionBody(script, "readControls"));
        foreach (var name in new[] { "submitPreview", "readControls", "reloadPreview", "updateIframePreview", "openComponentInNewWindow" })
        {
            var body = FunctionBody(script, name);
            Assert.DoesNotContain("localStorage", body);
            Assert.DoesNotContain("sessionStorage", body);
            Assert.DoesNotContain("window.name", body);
            Assert.DoesNotContain("location", body);
        }

        Assert.DoesNotContain("postMessage", script);
    }

    [Fact]
    public async Task A_viewport_change_reloads_the_preview_by_re_posting_the_controls()
    {
        var script = await PageAssets.LoadScriptAsync();
        var viewport = FunctionBody(script, "changeViewport");
        var reload = FunctionBody(script, "reloadPreview");

        Assert.Contains("reloadPreview();", viewport);
        Assert.DoesNotContain(".src", viewport);

        // With a Controls panel the reload is the protected POST of the current values; only a
        // component with no controls, and so no edits, reloads its GET URL.
        Assert.Matches(@"if \(document\.getElementById\('component-form'\)\) \{\s*updateIframePreview\(\);\s*return;\s*\}", reload);
    }

    [Fact]
    public async Task There_is_one_reset_button_and_it_says_what_it_resets_to()
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client);
        var script = await PageAssets.LoadScriptAsync();

        var reset = Assert.Single(ResetButton().Matches(html));
        Assert.Equal("Reset to story values", reset.Groups[1].Value);
        Assert.DoesNotContain("reset-story", html);
        Assert.DoesNotContain("reset-story", script);
        Assert.DoesNotContain("location.reload", script);
        Assert.Contains("form.reset();", FunctionBody(script, "resetForm"));
    }

    public static TheoryData<bool, int, string> ResetLabels => new()
    {
        { false, 0, "Reset to defaults" },
        { true, 0, "Reset to defaults" },
        { true, 1, "Reset to story values" }
    };

    [Theory]
    [MemberData(nameof(ResetLabels))]
    public void The_reset_label_follows_what_the_controls_were_rendered_with(bool hasStory, int storyProps, string expected)
    {
        // ResetLabel reads only the selected story, so the model needs none of its services.
        var model = (RazorKitPageModel)RuntimeHelpers.GetUninitializedObject(typeof(RazorKitPageModel));
        if (hasStory)
        {
            var story = new ComponentStory { Name = "s", DisplayName = "S" };
            for (var i = 0; i < storyProps; i++)
            {
                story.Properties["P" + i] = "v";
            }

            typeof(RazorKitPageModel).GetProperty(nameof(RazorKitPageModel.SelectedStory), BindingFlags.Public | BindingFlags.Instance)!
                .SetValue(model, story);
        }

        Assert.Equal(expected, model.ResetLabel);
    }

    // The headers Chromium sent for "Open in new window" in the walkthrough: a top-level form
    // navigation from the explorer page.
    private static void NewWindowNavigation(System.Net.Http.Headers.HttpRequestHeaders headers, string fetchSite)
    {
        headers.Add("Sec-Fetch-Site", fetchSite);
        headers.Add("Sec-Fetch-Mode", "navigate");
        headers.Add("Sec-Fetch-Dest", "document");
        headers.Add("Sec-Fetch-User", "?1");
        headers.Add("Origin", "http://localhost");
    }

    [Fact]
    public async Task A_same_origin_new_window_post_renders_the_edited_props()
    {
        await using var host = await TestHost.StartAsync();
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, "{\"Label\":\"Edited in the panel\"}",
            h => NewWindowNavigation(h, "same-origin")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<button type=\"button\">Edited in the panel</button>", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    [InlineData("none")]
    public async Task A_new_window_post_from_anywhere_else_is_rejected_like_any_render(string fetchSite)
    {
        await using var host = await TestHost.StartAsync();
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, "{\"Label\":\"Edited in the panel\"}",
            h => NewWindowNavigation(h, fetchSite)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("fetch-site", RejectCode(response));
    }

    [Fact]
    public async Task A_new_window_post_without_the_token_is_rejected()
    {
        await using var host = await TestHost.StartAsync();
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, field: null, configure: h => NewWindowNavigation(h, "same-origin")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("token-mismatch", RejectCode(response));
    }

    [Fact]
    public async Task An_empty_props_post_renders_the_story_values()
    {
        // "Open" for a component without controls posts an empty propsJson.
        await using var host = await TestHost.StartAsync();
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, propsJson: string.Empty,
            configure: h => NewWindowNavigation(h, "same-origin")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<button type=\"button\">Click me</button>", await response.Content.ReadAsStringAsync());
    }
}
