using System.Net;
using System.Text.RegularExpressions;
using Manifesto.RazorKit.Extensions;
using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Services;
using Manifesto.RazorKit.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// Configured themes: validated at startup, chosen only by an exact configured name, rendered by
/// the server as the configured data-theme, and carried as ?theme= on RazorKit's own links.
/// </summary>
public partial class ThemeTests
{
    private static readonly RazorKitTheme[] TwoThemes =
    {
        new() { Name = "Light", DataTheme = "light-theme" },
        new() { Name = "Dark", DataTheme = "dark_theme" }
    };

    [GeneratedRegex("<html[^>]*>")]
    private static partial Regex HtmlStartTag();

    [GeneratedRegex("<select id=\"theme-selector\"[^>]*>(.*?)</select>", RegexOptions.Singleline)]
    private static partial Regex ThemeSelector();

    [GeneratedRegex("<option value=\"([^\"]*)\"( selected=\"selected\")?>([^<]*)</option>")]
    private static partial Regex ThemeOption();

    [GeneratedRegex("<a href=\"([^\"]*)\"\\s+data-razorkit-themed-link")]
    private static partial Regex ThemedLink();

    [GeneratedRegex("<iframe id=\"preview-iframe\"\\s+name=\"preview-iframe\"\\s+src=\"([^\"]*)\"")]
    private static partial Regex IframeSrc();

    private static Task<TestHost> StartWithThemes(params RazorKitTheme[] themes) =>
        TestHost.StartAsync(new TestHostOptions { ConfigureRazorKit = o => o.Themes = themes.ToList() });

    private static async Task<string> GetPreviewHtmlTag(HttpClient client, string query = "")
    {
        var response = await client.GetAsync(PreviewPath + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        return HtmlStartTag().Match(html).Value;
    }

    // ---- Options validation ----

    [Fact]
    public void No_themes_is_valid_and_has_no_default()
    {
        var set = RazorKitThemeSet.Create(new RazorKitOptions().Themes);

        Assert.Empty(set.Themes);
        Assert.Null(set.Default);
        Assert.False(set.HasChoice);
        Assert.Null(set.Resolve(new StringValues("anything")));
    }

    [Fact]
    public void A_null_themes_list_is_treated_as_none()
    {
        Assert.Empty(RazorKitThemeSet.Create(null).Themes);
    }

    [Fact]
    public void Valid_themes_are_kept_in_order_and_the_first_is_the_default()
    {
        var set = RazorKitThemeSet.Create(TwoThemes);

        Assert.Equal(new[] { "Light", "Dark" }, set.Themes.Select(t => t.Name));
        Assert.Equal("light-theme", set.Default!.DataTheme);
        Assert.True(set.HasChoice);
    }

    public static TheoryData<string, string> InvalidThemes => new()
    {
        { "", "light" },
        { "   ", "light" },
        { " Light", "light" },
        { "Light ", "light" },
        { "Li\nght", "light" },
        { "Li\u0000ght", "light" },
        { new string('a', 65), "light" },
        { "Light", "" },
        { "Light", "light theme" },
        { "Light", "light\"" },
        { "Light", "light'" },
        { "Light", "<light>" },
        { "Light", "light;color:red" },
        { "Light", "light&amp;" },
        { "Light", "light\n" },
        { "Light", "light\r\n" },
        { "Light", "light.theme" },
        { "Light", "lïght" },
        { "Light", new string('a', 65) }
    };

    [Theory]
    [MemberData(nameof(InvalidThemes))]
    public void An_invalid_theme_stops_startup(string name, string dataTheme)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() =>
            services.AddRazorKit(o => o.Themes.Add(new RazorKitTheme { Name = name, DataTheme = dataTheme })));
        Assert.StartsWith("RazorKit Themes entry 0", ex.Message);
    }

    [Theory]
    [InlineData("Light", "Light")]
    [InlineData("Light", "LIGHT")]
    public void Duplicate_names_ignoring_case_stop_startup(string first, string second)
    {
        var ex = Assert.Throws<ArgumentException>(() => RazorKitThemeSet.Create(new[]
        {
            new RazorKitTheme { Name = first, DataTheme = "a" },
            new RazorKitTheme { Name = second, DataTheme = "b" }
        }));
        Assert.Contains("entry 1", ex.Message);
    }

    [Fact]
    public void A_null_entry_stops_startup()
    {
        Assert.Throws<ArgumentException>(() => RazorKitThemeSet.Create(new RazorKitTheme?[] { null }));
    }

    [Fact]
    public void Themes_bind_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RazorKit:Themes:0:Name"] = "Light",
            ["RazorKit:Themes:0:DataTheme"] = "light-theme",
            ["RazorKit:Themes:1:Name"] = "Dark",
            ["RazorKit:Themes:1:DataTheme"] = "dark_theme"
        }).Build();

        var options = new RazorKitOptions();
        configuration.GetSection("RazorKit").Bind(options);

        var set = RazorKitThemeSet.Create(options.Themes);
        Assert.Equal(new[] { ("Light", "light-theme"), ("Dark", "dark_theme") }, set.Themes.Select(t => (t.Name, t.DataTheme)));
    }

    [Fact]
    public async Task Changing_the_options_after_startup_does_not_change_the_preview()
    {
        RazorKitOptions? captured = null;
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigureRazorKit = o =>
            {
                o.Themes.Add(new RazorKitTheme { Name = "Light", DataTheme = "light-theme" });
                captured = o;
            }
        });

        captured!.Themes[0].DataTheme = "\"><script>alert(1)</script>";
        captured.Themes.Insert(0, new RazorKitTheme { Name = "Other", DataTheme = "other" });

        Assert.Equal("<html lang=\"en\" data-theme=\"light-theme\">", await GetPreviewHtmlTag(host.Client));
    }

    // ---- Exact-match resolution ----

    public static TheoryData<string?> NotAConfiguredName => new()
    {
        null,
        "",
        "dark",
        "DARK",
        " Dark",
        "Dark ",
        "dark_theme",
        "Unknown",
        "\"><script>alert(1)</script>",
        "Dark\u0000"
    };

    [Theory]
    [MemberData(nameof(NotAConfiguredName))]
    public void Anything_but_an_exact_configured_name_resolves_to_the_default(string? requested)
    {
        var set = RazorKitThemeSet.Create(TwoThemes);

        Assert.Null(set.FindRequested(new StringValues(requested)));
        Assert.Equal("Light", set.Resolve(new StringValues(requested))!.Name);
    }

    [Fact]
    public void A_repeated_theme_parameter_is_not_a_choice()
    {
        var set = RazorKitThemeSet.Create(TwoThemes);

        Assert.Null(set.FindRequested(new StringValues(new[] { "Dark", "Dark" })));
        Assert.Equal("Dark", set.FindRequested(new StringValues("Dark"))!.Name);
    }

    // ---- The preview document ----

    [Fact]
    public async Task With_no_themes_the_preview_sets_no_theme()
    {
        await using var host = await TestHost.StartAsync();

        Assert.Equal("<html lang=\"en\">", await GetPreviewHtmlTag(host.Client));
        Assert.Equal("<html lang=\"en\">", await GetPreviewHtmlTag(host.Client, "?theme=Dark"));
    }

    [Fact]
    public async Task One_theme_is_always_applied()
    {
        await using var host = await StartWithThemes(new RazorKitTheme { Name = "Only", DataTheme = "only" });

        Assert.Equal("<html lang=\"en\" data-theme=\"only\">", await GetPreviewHtmlTag(host.Client));
        Assert.Equal("<html lang=\"en\" data-theme=\"only\">", await GetPreviewHtmlTag(host.Client, "?theme=Other"));
    }

    [Theory]
    [InlineData("", "light-theme")]
    [InlineData("?theme=Light", "light-theme")]
    [InlineData("?theme=Dark", "dark_theme")]
    [InlineData("?theme=dark", "light-theme")]
    [InlineData("?theme=dark_theme", "light-theme")]
    [InlineData("?theme=Dark&theme=Dark", "light-theme")]
    [InlineData("?theme=", "light-theme")]
    public async Task The_preview_get_renders_the_configured_value_for_the_requested_name(string query, string expected)
    {
        await using var host = await StartWithThemes(TwoThemes);

        Assert.Equal($"<html lang=\"en\" data-theme=\"{expected}\">", await GetPreviewHtmlTag(host.Client, query));
    }

    [Theory]
    [InlineData("\"><script>alert('rk-echo')</script>")]
    [InlineData("rk-echo' onload='alert(1)")]
    [InlineData("javascript:rk-echo")]
    public async Task An_injection_shaped_theme_falls_back_and_is_never_echoed(string requested)
    {
        await using var host = await StartWithThemes(TwoThemes);
        var query = "?theme=" + Uri.EscapeDataString(requested);

        var preview = await (await host.Client.GetAsync(PreviewPath + query)).Content.ReadAsStringAsync();
        var shell = await (await host.Client.GetAsync(ShellPath + query)).Content.ReadAsStringAsync();

        Assert.Contains("<html lang=\"en\" data-theme=\"light-theme\">", preview);
        foreach (var html in new[] { preview, shell })
        {
            Assert.DoesNotContain("rk-echo", html);
        }

        Assert.Equal("Light", PageAssets.Data(shell).GetProperty("theme").GetString());
        Assert.False(PageAssets.Data(shell).GetProperty("themeRequested").GetBoolean());
    }

    [Fact]
    public async Task The_render_post_renders_the_theme_from_its_query_string()
    {
        await using var host = await StartWithThemes(TwoThemes);
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, path: PreviewPath + "?theme=Dark"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"en\" data-theme=\"dark_theme\">", html);
        Assert.Contains("Posted label", html);
    }

    [Fact]
    public async Task The_theme_does_not_bypass_request_protection()
    {
        await using var host = await StartWithThemes(TwoThemes);
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, field: null, path: PreviewPath + "?theme=Dark"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("token-mismatch", RejectCode(response));
    }

    [Fact]
    public async Task An_error_preview_is_themed_too()
    {
        await using var host = await StartWithThemes(TwoThemes);

        var html = await (await host.Client.GetAsync("/razorkit-preview/preview/no-such-component/default?theme=Dark")).Content.ReadAsStringAsync();

        Assert.Contains("<html lang=\"en\" data-theme=\"dark_theme\">", html);
        Assert.Contains("Component not found.", html);
    }

    [Fact]
    public async Task No_preview_script_reads_a_theme_from_storage()
    {
        await using var host = await StartWithThemes(TwoThemes);

        var html = await (await host.Client.GetAsync(PreviewPath)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("localStorage", html);
        Assert.DoesNotContain("storybook-theme", html);
        Assert.DoesNotContain("charity", html, StringComparison.OrdinalIgnoreCase);
    }

    // ---- The explorer page ----

    [Fact]
    public async Task With_no_themes_there_is_no_dropdown_and_links_carry_no_theme()
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client);
        var data = PageAssets.Data(html);

        Assert.DoesNotContain("theme-selector", html);
        Assert.DoesNotContain("?theme=", html);
        Assert.Equal(0, data.GetProperty("themes").GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("theme").ValueKind);
    }

    [Fact]
    public async Task With_one_theme_there_is_no_dropdown_and_links_carry_no_theme()
    {
        await using var host = await StartWithThemes(new RazorKitTheme { Name = "Only", DataTheme = "only" });

        var html = await PageAssets.LoadPageAsync(host.Client);

        Assert.DoesNotContain("theme-selector", html);
        Assert.DoesNotContain("?theme=", html);
        Assert.Equal(0, PageAssets.Data(html).GetProperty("themes").GetArrayLength());
    }

    [Fact]
    public async Task With_two_themes_the_dropdown_lists_them_with_the_default_selected()
    {
        await using var host = await StartWithThemes(TwoThemes);

        var html = await PageAssets.LoadPageAsync(host.Client);
        var select = ThemeSelector().Match(html);

        Assert.True(select.Success, "The theme dropdown is missing.");
        Assert.Contains("aria-label=\"Preview theme\"", select.Value);
        var options = ThemeOption().Matches(select.Groups[1].Value);
        Assert.Equal(new[] { ("Light", true), ("Dark", false) },
            options.Select(o => (o.Groups[1].Value, o.Groups[2].Success)));
        Assert.Equal(new[] { "Light", "Dark" }, options.Select(o => o.Groups[3].Value));
    }

    [Fact]
    public async Task The_requested_theme_is_selected_and_carried_on_every_link()
    {
        await using var host = await StartWithThemes(TwoThemes);

        var html = await PageAssets.LoadPageAsync(host.Client, ShellPath + "?theme=Dark");
        var data = PageAssets.Data(html);

        Assert.Contains("<option value=\"Dark\" selected=\"selected\">Dark</option>", html);
        Assert.Equal("/razorkit-preview/preview/button/default?theme=Dark", IframeSrc().Match(html).Groups[1].Value);
        var links = ThemedLink().Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(links);
        Assert.All(links, link => Assert.EndsWith("?theme=Dark", link));
        Assert.Equal(new[] { "Light", "Dark" }, data.GetProperty("themes").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("Dark", data.GetProperty("theme").GetString());
        Assert.True(data.GetProperty("themeRequested").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?theme=dark")]
    [InlineData("?theme=Nope")]
    public async Task Without_a_valid_requested_theme_the_page_uses_the_default_and_says_so(string query)
    {
        await using var host = await StartWithThemes(TwoThemes);

        var html = await PageAssets.LoadPageAsync(host.Client, ShellPath + query);
        var data = PageAssets.Data(html);

        Assert.Equal("/razorkit-preview/preview/button/default?theme=Light", IframeSrc().Match(html).Groups[1].Value);
        Assert.Equal("Light", data.GetProperty("theme").GetString());
        Assert.False(data.GetProperty("themeRequested").GetBoolean());
    }

    [Fact]
    public async Task A_theme_name_is_url_encoded_in_links_and_html_encoded_in_the_dropdown()
    {
        await using var host = await StartWithThemes(
            new RazorKitTheme { Name = "Tea & Cake", DataTheme = "tea" },
            new RazorKitTheme { Name = "Pink <b>", DataTheme = "pink" });

        var html = await PageAssets.LoadPageAsync(host.Client, ShellPath + "?theme=" + Uri.EscapeDataString("Pink <b>"));

        Assert.Contains("<option value=\"Tea &amp; Cake\">Tea &amp; Cake</option>", html);
        Assert.Contains("<option value=\"Pink &lt;b&gt;\" selected=\"selected\">Pink &lt;b&gt;</option>", html);
        Assert.Contains("?theme=Pink%20%3Cb%3E", html);
        Assert.DoesNotContain("<b>", html);

        var preview = await GetPreviewHtmlTag(host.Client, "?theme=" + Uri.EscapeDataString("Tea & Cake"));
        Assert.Equal("<html lang=\"en\" data-theme=\"tea\">", preview);
    }

    [Theory]
    [InlineData("/razorkit?theme=Dark", "/razorkit/button/default?theme=Dark")]
    [InlineData("/razorkit/button?theme=Dark", "/razorkit/button/default?theme=Dark")]
    [InlineData("/razorkit?theme=dark", "/razorkit/button/default")]
    [InlineData("/razorkit", "/razorkit/button/default")]
    public async Task Redirects_keep_only_a_valid_requested_theme(string path, string expected)
    {
        await using var host = await StartWithThemes(TwoThemes);

        var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location!.OriginalString);
    }

    // ---- The page script ----

    private static string FunctionBody(string script, string name)
    {
        var start = script.IndexOf($"function {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} was not found in the page script.");
        var next = script.IndexOf("\nfunction ", start + 1, StringComparison.Ordinal);
        return next < 0 ? script[start..] : script[start..next];
    }

    [Fact]
    public async Task Every_preview_request_the_script_makes_carries_the_theme()
    {
        var script = await PageAssets.LoadScriptAsync();

        Assert.Contains("form.action = withTheme(url);", FunctionBody(script, "submitPreview"));
        Assert.Contains("const src = withTheme(iframe.dataset.previewSrc);", FunctionBody(script, "reloadPreview"));
        Assert.Contains("parsed.searchParams.set('theme', currentTheme);", FunctionBody(script, "withTheme"));
    }

    [Fact]
    public async Task Open_in_new_window_uses_the_captured_preview_url_not_the_blanked_src()
    {
        var script = await PageAssets.LoadScriptAsync();

        Assert.Contains(
            "submitPreview('_blank', iframe.dataset.previewSrc || iframe.getAttribute('src'), '');",
            FunctionBody(script, "openComponentInNewWindow"));
    }

    [Fact]
    public async Task Switching_theme_reloads_the_preview_without_touching_the_frame()
    {
        var script = await PageAssets.LoadScriptAsync();
        var apply = FunctionBody(script, "applyTheme");

        Assert.Contains("reloadPreview();", apply);
        Assert.Contains("[data-razorkit-themed-link]", apply);
        Assert.DoesNotContain("contentDocument", apply);
        Assert.DoesNotContain("contentWindow", apply);
        Assert.DoesNotContain("setAttribute('data-theme'", script);
        Assert.Contains("applyTheme(evt.target.value);", script);
    }

    [Fact]
    public async Task Storage_holds_only_a_configured_theme_name()
    {
        var script = await PageAssets.LoadScriptAsync();

        Assert.Contains("return isConfiguredTheme(stored) ? stored : null;", FunctionBody(script, "readStoredTheme"));
        Assert.Contains("localStorage.setItem(themeStorageKey, name);", FunctionBody(script, "storeTheme"));
        Assert.Contains("if (!isConfiguredTheme(name)) return;", FunctionBody(script, "applyTheme"));
        Assert.Contains("razorKitData.themes.indexOf(name) !== -1", FunctionBody(script, "isConfiguredTheme"));

        // The theme functions are the only ones that touch the theme key.
        var themeKeyUses = Regex.Matches(script, "themeStorageKey").Count;
        Assert.Equal(3, themeKeyUses);
    }

    [Fact]
    public async Task A_remembered_theme_is_used_only_when_the_url_names_none()
    {
        var script = await PageAssets.LoadScriptAsync();
        var init = FunctionBody(script, "initializeTheme");

        Assert.Matches(@"if \(razorKitData\.themeRequested\) \{\s*// [^\n]*\n\s*storeTheme\(currentTheme\);\s*return;\s*\}", init);
        Assert.Contains("const stored = readStoredTheme();", init);
        Assert.Contains("applyTheme(stored);", init);
    }

    [Fact]
    public async Task Nothing_in_the_package_defaults_to_an_old_theme()
    {
        var script = await PageAssets.LoadScriptAsync();

        Assert.DoesNotContain("charity", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storybook-theme", script);
    }
}
