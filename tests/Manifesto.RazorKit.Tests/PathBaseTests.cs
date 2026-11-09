using System.Net;
using System.Text.RegularExpressions;
using Manifesto.RazorKit.Helpers;
using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// A host mounted under a path base: every URL RazorKit writes, in the explorer page, its data
/// block, its redirects and the preview document, is under that base.
/// </summary>
public partial class PathBaseTests
{
    private const string PathBase = "/site";

    private static readonly RazorKitTheme[] TwoThemes =
    {
        new() { Name = "Light", DataTheme = "light-theme" },
        new() { Name = "Dark", DataTheme = "dark_theme" }
    };

    // Every URL-bearing attribute RazorKit's markup uses.
    [GeneratedRegex("\\s(?:href|src|action)=\"([^\"]*)\"")]
    private static partial Regex UrlAttribute();

    private static IReadOnlyList<string> Urls(string html) => UrlAttribute().Matches(html)
        .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
        .ToList();

    private static Task<TestHost> StartUnder(string pathBase, Action<RazorKitOptions>? configure = null) =>
        TestHost.StartAsync(new TestHostOptions
        {
            ConfigureRazorKit = configure,
            ConfigurePipeline = app => app.UsePathBase(pathBase)
        });

    private static async Task<string> GetOkAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static void AllUnder(string pathBase, IReadOnlyList<string> urls)
    {
        Assert.NotEmpty(urls);
        foreach (var url in urls)
        {
            Assert.StartsWith(pathBase + "/", url);
            Assert.False(url.StartsWith("//", StringComparison.Ordinal), $"'{url}' is scheme-relative.");
        }
    }

    // ---- The resolver ----

    [Theory]
    [InlineData("", "/css/main.css", "/css/main.css")]
    [InlineData("", "~/css/main.css", "/css/main.css")]
    [InlineData("/site", "/css/main.css", "/site/css/main.css")]
    [InlineData("/site", "~/css/main.css", "/site/css/main.css")]
    [InlineData("/site", "/css/main.css?v=1", "/site/css/main.css?v=1")]
    [InlineData("/my site", "/x.css", "/my%20site/x.css")]
    public void An_app_relative_path_resolves_against_the_path_base(string pathBase, string path, string expected) =>
        Assert.Equal(expected, AppPath.Resolve(new PathString(pathBase), path));

    // ---- Explorer page ----

    [Fact]
    public async Task Every_url_in_the_explorer_page_is_under_the_path_base()
    {
        await using var host = await StartUnder(PathBase);

        var html = await GetOkAsync(host.Client, PathBase + ShellPath);
        var urls = Urls(html);

        AllUnder(PathBase, urls);
        Assert.Contains("/site/css/main.css", urls);
        Assert.Contains("/site/razorkit/button/default", urls);
        Assert.Contains("/site/razorkit-preview/preview/button/default", urls);
    }

    [Fact]
    public async Task Every_url_in_the_themed_explorer_page_is_under_the_path_base()
    {
        await using var host = await StartUnder(PathBase, o => o.Themes = TwoThemes.ToList());

        var html = await GetOkAsync(host.Client, PathBase + ShellPath + "?theme=Dark");
        var urls = Urls(html);

        AllUnder(PathBase, urls);
        Assert.Contains("/site/razorkit/button/default?theme=Dark", urls);
        Assert.Contains("/site/razorkit-preview/preview/button/default?theme=Dark", urls);
    }

    [Fact]
    public async Task Configured_explorer_stylesheets_are_under_the_path_base()
    {
        await using var host = await StartUnder(PathBase, o => o.ExplorerStylesheets = new List<string> { "~/dist/a.css", "/dist/b.css" });

        var urls = Urls(await GetOkAsync(host.Client, PathBase + ShellPath));

        AllUnder(PathBase, urls);
        Assert.Contains("/site/dist/a.css", urls);
        Assert.Contains("/site/dist/b.css", urls);
    }

    [Fact]
    public async Task The_data_block_gives_the_script_the_preview_url_under_the_path_base()
    {
        await using var host = await StartUnder(PathBase);

        var data = PageAssets.Data(await GetOkAsync(host.Client, PathBase + ShellPath));

        Assert.Equal("/site/razorkit-preview/preview", data.GetProperty("previewUrl").GetString());
    }

    [Fact]
    public async Task At_the_root_the_data_block_preview_url_is_root_relative()
    {
        await using var host = await TestHost.StartAsync();

        var data = PageAssets.Data(await GetOkAsync(host.Client, ShellPath));

        Assert.Equal("/razorkit-preview/preview", data.GetProperty("previewUrl").GetString());
    }

    // ---- Redirects ----

    [Theory]
    [InlineData("/razorkit", "/site/razorkit/button/default")]
    [InlineData("/razorkit/button", "/site/razorkit/button/default")]
    public async Task Redirects_stay_under_the_path_base(string path, string expected)
    {
        await using var host = await StartUnder(PathBase);

        var response = await host.Client.GetAsync(PathBase + path);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_themed_redirect_stays_under_the_path_base()
    {
        await using var host = await StartUnder(PathBase, o => o.Themes = TwoThemes.ToList());

        var response = await host.Client.GetAsync(PathBase + "/razorkit?theme=Dark");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/site/razorkit/button/default?theme=Dark", response.Headers.Location!.OriginalString);
    }

    // ---- Preview ----

    [Fact]
    public async Task Every_url_in_the_preview_is_under_the_path_base()
    {
        await using var host = await StartUnder(PathBase);

        var urls = Urls(await GetOkAsync(host.Client, PathBase + PreviewPath));

        AllUnder(PathBase, urls);
        Assert.Contains("/site/_content/Fixture.Components/css/main.css", urls);
        Assert.Contains("/site/_content/Fixture.Components/js/main.js", urls);
    }

    [Fact]
    public async Task Every_url_in_the_posted_preview_is_under_the_path_base()
    {
        await using var host = await StartUnder(PathBase);

        var request = WithValidProtection(new HttpRequestMessage(HttpMethod.Post, PathBase + PreviewPath),
            new[] { new KeyValuePair<string, string>("propsJson", "{\"Label\":\"Posted\"}") });
        var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Posted", html);
        AllUnder(PathBase, Urls(html));
    }

    [Fact]
    public async Task Configured_preview_assets_are_under_the_path_base()
    {
        await using var host = await StartUnder(PathBase, o =>
        {
            o.PreviewStylesheets = new List<string> { "~/dist/a.css" };
            o.PreviewScripts = new List<string> { "/dist/a.js" };
        });

        var urls = Urls(await GetOkAsync(host.Client, PathBase + PreviewPath));

        AllUnder(PathBase, urls);
        Assert.Contains("/site/dist/a.css", urls);
        Assert.Contains("/site/dist/a.js", urls);
    }

    [Fact]
    public async Task At_the_root_every_url_is_root_relative()
    {
        await using var host = await TestHost.StartAsync();

        var page = Urls(await GetOkAsync(host.Client, ShellPath));
        var preview = Urls(await GetOkAsync(host.Client, PreviewPath));

        foreach (var url in page.Concat(preview))
        {
            Assert.StartsWith("/", url);
            Assert.False(url.StartsWith("//", StringComparison.Ordinal), $"'{url}' is scheme-relative.");
        }
    }

    // ---- Hostile path base ----

    // A host that copies X-Forwarded-Prefix into PathBase hands RazorKit whatever the client sent.
    // The middleware sets PathBase directly because UsePathBase would need the same prefix on the
    // request path, which an HttpClient cannot send as "//host/...".
    private static Task<TestHost> StartWithRawPathBase(string pathBase) =>
        TestHost.StartAsync(new TestHostOptions
        {
            ConfigurePipeline = app => app.Use((context, next) =>
            {
                context.Request.PathBase = new PathString(pathBase);
                return next();
            })
        });

    private static void NoneSchemeRelative(IEnumerable<string> urls)
    {
        var all = urls.ToList();
        Assert.NotEmpty(all);
        foreach (var url in all)
        {
            Assert.StartsWith("/", url);
            Assert.False(url.StartsWith("//", StringComparison.Ordinal), $"'{url}' is scheme-relative.");
        }
    }

    [Theory]
    [InlineData("//evil.com", "/evil.com/css/main.css")]
    [InlineData("///evil.com", "/evil.com/css/main.css")]
    [InlineData("/", "/css/main.css")]
    public void The_resolver_never_returns_a_scheme_relative_url(string pathBase, string expected) =>
        Assert.Equal(expected, AppPath.Resolve(new PathString(pathBase), "/css/main.css"));

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("///evil.com")]
    public async Task A_hostile_path_base_cannot_make_the_page_urls_scheme_relative(string pathBase)
    {
        await using var host = await StartWithRawPathBase(pathBase);

        var html = await GetOkAsync(host.Client, ShellPath);

        NoneSchemeRelative(Urls(html));
        var previewUrl = PageAssets.Data(html).GetProperty("previewUrl").GetString()!;
        Assert.StartsWith("/", previewUrl);
        Assert.False(previewUrl.StartsWith("//", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("///evil.com")]
    public async Task A_hostile_path_base_cannot_make_the_preview_urls_scheme_relative(string pathBase)
    {
        await using var host = await StartWithRawPathBase(pathBase);

        NoneSchemeRelative(Urls(await GetOkAsync(host.Client, PreviewPath)));

        var request = WithValidProtection(new HttpRequestMessage(HttpMethod.Post, PreviewPath),
            new[] { new KeyValuePair<string, string>("propsJson", "{\"Label\":\"Posted\"}") });
        var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        NoneSchemeRelative(Urls(await response.Content.ReadAsStringAsync()));
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("///evil.com")]
    public async Task A_hostile_path_base_cannot_make_a_redirect_leave_the_origin(string pathBase)
    {
        await using var host = await StartWithRawPathBase(pathBase);

        var response = await host.Client.GetAsync("/razorkit");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/", location);
        Assert.False(location.StartsWith("//", StringComparison.Ordinal), $"'{location}' is scheme-relative.");
    }

    // ---- Explorer assets ----

    [Fact]
    public async Task The_explorer_own_files_get_an_escaped_href_under_a_path_base_with_a_space()
    {
        await using var host = await StartWithRawPathBase("/my site");

        var urls = Urls(await GetOkAsync(host.Client, ShellPath));

        Assert.Contains(urls, u => u.StartsWith("/my%20site/_content/Manifesto.RazorKit/css/razorkit.css?v=", StringComparison.Ordinal));
        Assert.Contains(urls, u => u.StartsWith("/my%20site/_content/Manifesto.RazorKit/js/razorkit.js?v=", StringComparison.Ordinal));
    }

    // ---- Script ----

    [Fact]
    public async Task The_script_builds_preview_urls_from_the_data_block_not_a_fixed_root()
    {
        var script = await PageAssets.LoadScriptAsync();

        Assert.DoesNotContain("/razorkit-preview", script);
        Assert.Contains("razorKitData.previewUrl", script);
    }
}
