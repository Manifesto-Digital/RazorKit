using System.Net;
using System.Text.RegularExpressions;
using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Tests.Support;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// RazorKit's own chrome (header, sidebar, controls) is operable from the keyboard and exposes its
/// state to assistive technology, and on narrow screens the sidebar and the controls are toggled
/// panels. Issue #6 and the 0.3.0 own-UI accessibility item.
/// </summary>
public partial class OwnAccessibilityTests
{
    private const string TypedPath = "/razorkit/typed/default";

    // Any start tag that names a RazorKit action, with its tag name.
    [GeneratedRegex("<([a-z][a-z0-9]*)\\b[^>]*\\bdata-razorkit-action=\"([^\"]+)\"[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ActionElement();

    [GeneratedRegex("<button\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ButtonStartTag();

    [GeneratedRegex("<iframe id=\"preview-iframe\"[^>]*\\btitle=\"([^\"]*)\"")]
    private static partial Regex IframeTitle();

    [GeneratedRegex("<button[^>]*class=\"razorkit-component-name[^\"]*\"[^>]*>")]
    private static partial Regex ComponentNameButton();

    [GeneratedRegex("aria-controls=\"([^\"]+)\"")]
    private static partial Regex AriaControls();

    [GeneratedRegex("aria-expanded=\"(true|false)\"")]
    private static partial Regex AriaExpanded();

    [GeneratedRegex("<label class=\"razorkit-form-label\" for=\"([^\"]+)\"")]
    private static partial Regex PropLabel();

    [GeneratedRegex("@media \\(max-width: (\\d+)px\\)")]
    private static partial Regex NarrowMedia();

    [GeneratedRegex("matchMedia\\('\\(max-width: (\\d+)px\\)'\\)")]
    private static partial Regex NarrowQuery();

    private static async Task<string> LoadStylesheetAsync()
    {
        await using var host = await TestHost.StartAsync(PageAssets.WithStaticFiles());
        var html = await PageAssets.LoadPageAsync(host.Client);
        var response = await host.Client.GetAsync(PageAssets.StylesheetUrl(html));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string Tag(string html, string id)
    {
        var match = Regex.Match(html, "<[a-z]+\\b[^>]*\\bid=\"" + Regex.Escape(id) + "\"[^>]*>");
        Assert.True(match.Success, $"No element with id '{id}'.");
        return match.Value;
    }

    [Fact]
    public async Task Every_element_with_an_action_is_a_real_button()
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client);
        var actions = ActionElement().Matches(html);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.Equal("button", a.Groups[1].Value, ignoreCase: true));
        Assert.Contains(actions, a => a.Groups[2].Value == "toggle-stories");
        Assert.Contains(actions, a => a.Groups[2].Value == "toggle-a11y");
    }

    [Fact]
    public async Task Every_button_is_type_button_so_none_submits_the_controls_form()
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client);

        Assert.All(ButtonStartTag().Matches(html), b => Assert.Contains("type=\"button\"", b.Value));
    }

    [Fact]
    public async Task The_preview_frame_is_titled_with_the_component_and_story()
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client);
        var title = IframeTitle().Match(html);

        Assert.True(title.Success, "The preview iframe has no title.");
        Assert.Equal("Component preview: Button / Default", WebUtility.HtmlDecode(title.Groups[1].Value));
    }

    [Theory]
    [InlineData("razorkit-components-toggle", "components", "razorkit-sidebar")]
    [InlineData("razorkit-controls-toggle", "controls", "razorkit-controls")]
    public async Task Panel_toggles_are_collapsed_buttons_that_name_the_panel_they_control(string toggleId, string panel, string panelId)
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client);
        var toggle = Tag(html, toggleId);

        Assert.StartsWith("<button", toggle);
        Assert.Contains("type=\"button\"", toggle);
        Assert.Contains("data-razorkit-action=\"toggle-panel\"", toggle);
        Assert.Contains($"data-razorkit-panel=\"{panel}\"", toggle);
        Assert.Contains($"aria-controls=\"{panelId}\"", toggle);
        Assert.Contains("aria-expanded=\"false\"", toggle);
        Assert.Contains("aria-label=\"", Tag(html, panelId));
    }

    [Fact]
    public async Task Component_toggles_expose_their_story_list_and_state()
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client);
        var buttons = ComponentNameButton().Matches(html);

        Assert.NotEmpty(buttons);
        foreach (Match button in buttons)
        {
            var controls = AriaControls().Match(button.Value).Groups[1].Value;
            Assert.StartsWith("stories-", controls);
            Assert.Contains($"id=\"{controls}\"", html);

            // The selected component's stories render expanded, every other list collapsed.
            var expanded = AriaExpanded().Match(button.Value).Groups[1].Value;
            Assert.Equal(controls == "stories-button" ? "true" : "false", expanded);
        }
    }

    [Fact]
    public async Task The_accessibility_panel_header_is_a_collapsed_disclosure_button()
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client);
        var header = Regex.Match(html, "<[a-z]+[^>]*class=\"razorkit-a11y-header\"[^>]*>").Value;

        Assert.StartsWith("<button", header);
        Assert.Contains("aria-expanded=\"false\"", header);
        Assert.Contains("aria-controls=\"a11y-content\"", header);
        Assert.Contains("id=\"a11y-content\"", html);
    }

    [Fact]
    public async Task The_chrome_has_landmarks_and_a_named_width_selector()
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client);

        Assert.Contains("role=\"banner\"", html);
        Assert.Contains("role=\"navigation\"", Tag(html, "razorkit-sidebar"));
        Assert.Contains("<div class=\"razorkit-preview\" role=\"main\">", html);
        Assert.Contains("role=\"complementary\"", Tag(html, "razorkit-controls"));
        Assert.Contains("aria-label=\"Preview width\"", Tag(html, "viewport-selector"));
    }

    [Fact]
    public async Task Every_control_label_points_at_its_control()
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client, TypedPath);
        var labels = PropLabel().Matches(html);

        // Label, Count, Enabled and Mode: a text input, a text input, a checkbox and a select.
        Assert.Equal(4, labels.Count);
        foreach (Match label in labels)
        {
            var target = label.Groups[1].Value;
            Assert.StartsWith("razorkit-prop-", target);
            Assert.Single(Regex.Matches(html, $"\\bid=\"{Regex.Escape(target)}\""));
        }
    }

    [Fact]
    public async Task With_two_themes_the_theme_dropdown_has_a_visible_label()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigureRazorKit = o => o.Themes = [new RazorKitTheme { Name = "Light", DataTheme = "light" }, new RazorKitTheme { Name = "Dark", DataTheme = "dark" }]
        });

        var html = await PageAssets.LoadPageAsync(host.Client);

        Assert.Matches("<label class=\"razorkit-header-label\" for=\"theme-selector\">\\s*Theme\\s*</label>", html);
    }

    [Fact]
    public async Task The_script_keeps_panel_and_disclosure_state_in_aria_expanded()
    {
        var script = await PageAssets.LoadScriptAsync();

        Assert.Contains("case 'toggle-panel':", script);
        Assert.Contains("toggle.setAttribute('aria-expanded'", script);
        Assert.Contains("control.setAttribute('aria-expanded'", script);
        Assert.Contains("header.setAttribute('aria-expanded'", script);
        Assert.Contains("evt.key === 'Escape'", script);
        Assert.Contains("preview.inert", script);
        // The results panel's issue headers are built as buttons, not clickable divs.
        Assert.Contains("el('button', 'razorkit-a11y-issue-header')", script);
        Assert.DoesNotContain("el('div', 'razorkit-a11y-issue-header')", script);
    }

    [Fact]
    public async Task The_script_and_the_stylesheet_agree_on_the_narrow_breakpoint()
    {
        var script = await PageAssets.LoadScriptAsync();
        var css = await LoadStylesheetAsync();

        var query = NarrowQuery().Match(script);
        Assert.True(query.Success, "The script has no narrow-screen media query.");
        Assert.Contains(NarrowMedia().Matches(css), m => m.Groups[1].Value == query.Groups[1].Value);
    }

    [Fact]
    public async Task The_stylesheet_never_removes_the_focus_outline()
    {
        var css = await LoadStylesheetAsync();

        Assert.DoesNotContain("outline: none", css);
        Assert.Contains(":focus-visible", css);
    }
}
