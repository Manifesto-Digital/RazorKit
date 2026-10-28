using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Manifesto.RazorKit.Pages;
using Manifesto.RazorKit.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// The explorer page keeps its CSS and JS in package files and passes server data in a
/// non-executing JSON block, so no script runs from the page markup itself.
/// </summary>
public partial class PageAssetsTests
{
    [GeneratedRegex("<script\\b([^>]*)>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptElement();

    // An on* attribute inside any start tag, e.g. onclick= or onchange=.
    [GeneratedRegex("<[a-z][^>]*\\son[a-z]+\\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex InlineHandler();

    private static string ExpectedVersion =>
        Uri.EscapeDataString(typeof(RazorKitPageModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0]);

    [Theory]
    [InlineData("0.3.0+6b8d8f1abc", "0.3.0")]
    [InlineData("0.3.0-beta.1+sha.5114f85", "0.3.0-beta.1")]
    [InlineData("0.3.0", "0.3.0")]
    public void The_build_metadata_is_stripped_from_the_asset_version(string input, string expected) =>
        Assert.Equal(expected, RazorKitPageModel.StripBuildMetadata(input));

    [Fact]
    public async Task The_page_has_no_inline_event_handlers_or_javascript_urls()
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client);

        Assert.DoesNotMatch(InlineHandler(), html);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_only_inline_script_element_is_the_json_data_block()
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client);
        var scripts = ScriptElement().Matches(html);

        Assert.NotEmpty(scripts);
        var dataBlocks = 0;
        foreach (Match script in scripts)
        {
            var attributes = script.Groups[1].Value;
            if (attributes.Contains("type=\"application/json\"", StringComparison.Ordinal))
            {
                Assert.Contains("id=\"razorkit-data\"", attributes);
                dataBlocks++;
                continue;
            }

            // Anything else must be an external script with an empty body.
            Assert.Contains("src=\"", attributes);
            Assert.True(string.IsNullOrWhiteSpace(script.Groups[2].Value), "An executable script element has inline content.");
        }

        Assert.Equal(1, dataBlocks);
    }

    [Fact]
    public async Task The_data_block_carries_what_the_script_needs()
    {
        await using var host = await TestHost.StartAsync();

        var data = PageAssets.Data(await PageAssets.LoadPageAsync(host.Client));

        Assert.Equal("__RazorKitToken", data.GetProperty("tokenField").GetString());
        Assert.Equal(TokenField, data.GetProperty("tokenField").GetString());
        Assert.Equal("Button", data.GetProperty("componentName").GetString());
        Assert.Equal("~/Components/Atoms/Button/Button.cshtml", data.GetProperty("componentPath").GetString());

        var label = data.GetProperty("properties").GetProperty("Label");
        Assert.False(label.GetProperty("isCollection").GetBoolean());
        Assert.False(label.GetProperty("isComplexType").GetBoolean());
        Assert.Equal("String", label.GetProperty("typeName").GetString());
    }

    [Fact]
    public void Data_block_json_cannot_close_the_script_element()
    {
        var json = RazorKitPageModel.SerializePageData(new RazorKitPageData(
            "__RazorKitToken",
            "</script><script>alert(1)</script>",
            "<!-- & ' \"",
            new Dictionary<string, RazorKitPropertyData>()));

        Assert.DoesNotContain("<", json);
        Assert.DoesNotContain(">", json);
        Assert.DoesNotContain("&", json);

        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("</script><script>alert(1)</script>", parsed.RootElement.GetProperty("componentName").GetString());
        Assert.Equal("<!-- & ' \"", parsed.RootElement.GetProperty("componentPath").GetString());
    }

    [Fact]
    public async Task The_page_references_its_assets_with_the_package_version()
    {
        await using var host = await TestHost.StartAsync();

        var html = await PageAssets.LoadPageAsync(host.Client);

        Assert.DoesNotContain("%2B", PageAssets.ScriptUrl(html));
        Assert.Equal(PageAssets.ScriptPath + "?v=" + ExpectedVersion, PageAssets.ScriptUrl(html));
        Assert.Equal(PageAssets.StylesheetPath + "?v=" + ExpectedVersion, PageAssets.StylesheetUrl(html));
    }

    [Theory]
    [InlineData("/site")]
    [InlineData("")]
    public async Task The_assets_are_served_with_their_content_types_under_the_hosts_path_base(string pathBase)
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            EnvironmentName = Environments.Development,
            ConfigurePipeline = app =>
            {
                app.UsePathBase(pathBase);
                app.UseStaticFiles();
            }
        });

        var html = await PageAssets.LoadPageAsync(host.Client, pathBase + ShellPath);
        var scriptUrl = PageAssets.ScriptUrl(html);
        var stylesheetUrl = PageAssets.StylesheetUrl(html);

        Assert.StartsWith(pathBase + PageAssets.ScriptPath + "?v=", scriptUrl);
        Assert.StartsWith(pathBase + PageAssets.StylesheetPath + "?v=", stylesheetUrl);

        var script = await host.Client.GetAsync(scriptUrl);
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Contains("javascript", script.Content.Headers.ContentType?.MediaType);
        Assert.Contains("function postToIframe(", await script.Content.ReadAsStringAsync());

        var stylesheet = await host.Client.GetAsync(stylesheetUrl);
        Assert.Equal(HttpStatusCode.OK, stylesheet.StatusCode);
        Assert.Equal("text/css", stylesheet.Content.Headers.ContentType?.MediaType);
        Assert.Contains(".razorkit-page .razorkit-layout", await stylesheet.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_script_posts_the_token_from_the_meta_element_under_the_data_blocks_field_name()
    {
        var script = await PageAssets.LoadScriptAsync();

        Assert.Contains("document.querySelector('meta[name=\"razorkit-token\"]')", script);
        Assert.Contains("tokenInput.name = razorKitData.tokenField;", script);
        Assert.Contains("JSON.parse(document.getElementById('razorkit-data').textContent)", script);
    }

    [Fact]
    public async Task The_click_dispatcher_ignores_disabled_controls()
    {
        var script = await PageAssets.LoadScriptAsync();

        Assert.Contains("if (control.disabled || control.getAttribute('aria-disabled') === 'true') return;", script);
    }
}
