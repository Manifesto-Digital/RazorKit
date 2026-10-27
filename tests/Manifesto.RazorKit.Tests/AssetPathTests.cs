using System.Net;
using System.Text.RegularExpressions;
using Manifesto.RazorKit.Extensions;
using Manifesto.RazorKit.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// The host stylesheets and scripts RazorKit loads into the explorer page and the preview:
/// configurable, validated at startup as app-relative paths, and unchanged by default.
/// </summary>
public partial class AssetPathTests
{
    // Host stylesheet links only: RazorKit's own razorkit.css is excluded.
    [GeneratedRegex("<link rel=\"stylesheet\" href=\"([^\"]*)\" />")]
    private static partial Regex StylesheetLink();

    [GeneratedRegex("<script src=\"([^\"]*)\"></script>")]
    private static partial Regex ScriptTag();

    private static IReadOnlyList<string> HostStylesheets(string html) => StylesheetLink().Matches(html)
        .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
        .Where(href => !href.Contains("/_content/Manifesto.RazorKit/", StringComparison.Ordinal))
        .ToList();

    private static IReadOnlyList<string> HostScripts(string html) => ScriptTag().Matches(html)
        .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
        .Where(src => !src.Contains("/_content/Manifesto.RazorKit/", StringComparison.Ordinal))
        .ToList();

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static Task<TestHost> StartWith(Action<RazorKitOptions> configure) =>
        TestHost.StartAsync(new TestHostOptions { ConfigureRazorKit = configure });

    // ---- Defaults are today's behaviour ----

    [Fact]
    public async Task By_default_the_explorer_loads_the_sites_main_stylesheet_before_its_own()
    {
        await using var host = await TestHost.StartAsync();

        var html = await GetAsync(host.Client, ShellPath);

        Assert.Equal(new[] { "/css/main.css" }, HostStylesheets(html));
        Assert.True(html.IndexOf("href=\"/css/main.css\"", StringComparison.Ordinal)
                    < html.IndexOf("css/razorkit.css", StringComparison.Ordinal));
    }

    [Fact]
    public async Task By_default_the_preview_derives_its_assets_from_the_component_library_name()
    {
        await using var host = await TestHost.StartAsync();

        var html = await GetAsync(host.Client, PreviewPath);

        Assert.Equal(new[] { "/_content/Fixture.Components/css/main.css" }, HostStylesheets(html));
        Assert.Equal(new[] { "/_content/Fixture.Components/js/main.js" }, HostScripts(html));
    }

    [Fact]
    public async Task Without_a_component_library_name_the_preview_loads_no_host_assets()
    {
        await using var host = await StartWith(o => o.ComponentLibraryName = string.Empty);

        var html = await GetAsync(host.Client, PreviewPath);

        Assert.Empty(HostStylesheets(html));
        Assert.Empty(HostScripts(html));
    }

    [Fact]
    public void The_defaults_mean_derive_rather_than_none()
    {
        var options = new RazorKitOptions();

        Assert.Null(options.ExplorerStylesheets);
        Assert.Null(options.PreviewStylesheets);
        Assert.Null(options.PreviewScripts);
    }

    // ---- Explicit lists ----

    [Fact]
    public async Task Explicit_explorer_stylesheets_render_in_order()
    {
        await using var host = await StartWith(o => o.ExplorerStylesheets = new List<string> { "~/dist/site.css", "/dist/explorer.css?v=2" });

        var html = await GetAsync(host.Client, ShellPath);

        Assert.Equal(new[] { "/dist/site.css", "/dist/explorer.css?v=2" }, HostStylesheets(html));
    }

    [Fact]
    public async Task Explicit_preview_assets_render_in_order_and_replace_the_derived_ones()
    {
        await using var host = await StartWith(o =>
        {
            o.PreviewStylesheets = new List<string> { "/dist/a.css", "~/dist/b.css" };
            o.PreviewScripts = new List<string> { "~/dist/a.js", "/dist/b.js" };
        });

        var html = await GetAsync(host.Client, PreviewPath);

        Assert.Equal(new[] { "/dist/a.css", "/dist/b.css" }, HostStylesheets(html));
        Assert.Equal(new[] { "/dist/a.js", "/dist/b.js" }, HostScripts(html));
    }

    [Fact]
    public async Task Preview_stylesheets_and_scripts_are_configured_independently()
    {
        await using var host = await StartWith(o => o.PreviewScripts = new List<string> { "/dist/only.js" });

        var html = await GetAsync(host.Client, PreviewPath);

        Assert.Equal(new[] { "/_content/Fixture.Components/css/main.css" }, HostStylesheets(html));
        Assert.Equal(new[] { "/dist/only.js" }, HostScripts(html));
    }

    [Fact]
    public async Task An_empty_explorer_list_renders_no_host_stylesheet()
    {
        await using var host = await StartWith(o => o.ExplorerStylesheets = new List<string>());

        var html = await GetAsync(host.Client, ShellPath);

        Assert.Empty(HostStylesheets(html));
        Assert.Contains("css/razorkit.css", html);
    }

    [Fact]
    public async Task Empty_preview_lists_render_nothing_even_with_a_component_library_name()
    {
        await using var host = await StartWith(o =>
        {
            o.PreviewStylesheets = new List<string>();
            o.PreviewScripts = new List<string>();
        });

        var html = await GetAsync(host.Client, PreviewPath);

        Assert.Empty(HostStylesheets(html));
        Assert.Empty(HostScripts(html));
        Assert.Contains("lib/axe-core/axe.min.js", html);
    }

    [Fact]
    public async Task The_preview_error_page_carries_the_configured_assets_too()
    {
        await using var host = await StartWith(o => o.PreviewStylesheets = new List<string> { "/dist/a.css" });

        var html = await GetAsync(host.Client, "/razorkit-preview/preview/no-such-component/default");

        Assert.Equal(new[] { "/dist/a.css" }, HostStylesheets(html));
    }

    [Fact]
    public async Task Paths_are_html_attribute_encoded()
    {
        await using var host = await StartWith(o =>
        {
            o.ExplorerStylesheets = new List<string> { "/a.css?x=1&y=2" };
            o.PreviewScripts = new List<string> { "/a.js?x=1&y=2" };
        });

        var page = await GetAsync(host.Client, ShellPath);
        var preview = await GetAsync(host.Client, PreviewPath);

        Assert.Contains("href=\"/a.css?x=1&amp;y=2\"", page);
        Assert.Contains("src=\"/a.js?x=1&amp;y=2\"", preview);
    }

    [Fact]
    public async Task Changing_the_options_after_startup_changes_nothing()
    {
        RazorKitOptions? captured = null;
        await using var host = await StartWith(o =>
        {
            o.ExplorerStylesheets = new List<string> { "/a.css" };
            o.PreviewStylesheets = new List<string> { "/b.css" };
            captured = o;
        });

        captured!.ExplorerStylesheets![0] = "\"><script>alert(1)</script>";
        captured.PreviewStylesheets!.Add("//evil.example/x.css");

        Assert.Equal(new[] { "/a.css" }, HostStylesheets(await GetAsync(host.Client, ShellPath)));
        Assert.Equal(new[] { "/b.css" }, HostStylesheets(await GetAsync(host.Client, PreviewPath)));
    }

    // ---- Configuration binding ----

    [Fact]
    public void Configured_lists_replace_the_defaults_rather_than_appending_to_them()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RazorKit:ExplorerStylesheets:0"] = "/dist/site.css",
            ["RazorKit:PreviewStylesheets:0"] = "/dist/a.css",
            ["RazorKit:PreviewStylesheets:1"] = "~/dist/b.css",
            ["RazorKit:PreviewScripts:0"] = "/dist/a.js"
        }).Build();

        var options = new RazorKitOptions();
        configuration.GetSection("RazorKit").Bind(options);

        Assert.Equal(new[] { "/dist/site.css" }, options.ExplorerStylesheets);
        Assert.Equal(new[] { "/dist/a.css", "~/dist/b.css" }, options.PreviewStylesheets);
        Assert.Equal(new[] { "/dist/a.js" }, options.PreviewScripts);
    }

    // The README says an empty list cannot come from configuration; this pins that down so the
    // advice is revisited if the binder ever starts honouring an empty JSON array.
    [Fact]
    public void An_empty_json_array_leaves_the_default_in_place()
    {
        using var json = new MemoryStream("{\"RazorKit\":{\"PreviewScripts\":[]}}"u8.ToArray());
        var configuration = new ConfigurationBuilder().AddJsonStream(json).Build();

        var options = new RazorKitOptions();
        configuration.GetSection("RazorKit").Bind(options);

        Assert.Null(options.PreviewScripts);
    }

    // ---- Validation ----

    public static TheoryData<string> InvalidPaths => new()
    {
        "",
        "   ",
        "css/main.css",
        "https://cdn.example/main.css",
        "http://cdn.example/main.css",
        "javascript:alert(1)",
        "data:text/css,body{}",
        "//cdn.example/main.css",
        "~//cdn.example/main.css",
        "/\\cdn.example/main.css",
        "/css\\main.css",
        "~\\css/main.css",
        "/css/../secret.css",
        "/..",
        "~/../main.css",
        "/css/%2e%2e/secret.css",
        "/css/.%2E/secret.css",
        "/css/main.css\"",
        "/css/main.css'",
        "/css/<main>.css",
        "/css/main.css>",
        "/css/\u0000main.css",
        "/css/main\n.css",
        "/\t/cdn.example/main.css",
        "/css/main\u007f.css",
        "~",
        "~css/main.css"
    };

    private static readonly string[] ListOptionNames =
    {
        nameof(RazorKitOptions.ExplorerStylesheets),
        nameof(RazorKitOptions.PreviewStylesheets),
        nameof(RazorKitOptions.PreviewScripts)
    };

    public static TheoryData<string> ListOptions => new(ListOptionNames);

    private static void SetList(RazorKitOptions options, string option, IList<string> value)
    {
        switch (option)
        {
            case nameof(RazorKitOptions.ExplorerStylesheets): options.ExplorerStylesheets = value; break;
            case nameof(RazorKitOptions.PreviewStylesheets): options.PreviewStylesheets = value; break;
            case nameof(RazorKitOptions.PreviewScripts): options.PreviewScripts = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(option));
        }
    }

    [Theory]
    [MemberData(nameof(InvalidPaths))]
    public void An_invalid_path_stops_startup_naming_the_option(string path)
    {
        foreach (var option in ListOptionNames)
        {
            var services = new ServiceCollection();

            var ex = Assert.Throws<ArgumentException>(() => services.AddRazorKit(o =>
                SetList(o, option, new List<string> { "/fine.css", path })));
            Assert.StartsWith($"RazorKit {option} entry 1", ex.Message);
        }
    }

    [Theory]
    [MemberData(nameof(ListOptions))]
    public void A_null_entry_stops_startup(string option)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() => services.AddRazorKit(o =>
            SetList(o, option, new List<string> { null! })));
        Assert.StartsWith($"RazorKit {option} entry 0", ex.Message);
    }

    [Theory]
    [InlineData("/css/main.css")]
    [InlineData("~/css/main.css")]
    [InlineData("/css/main.css?v=1&b=2")]
    [InlineData("/css/main.css#top")]
    [InlineData("/css/./main.css")]
    [InlineData("/css/..main.css")]
    [InlineData("/css/main%20file.css")]
    [InlineData("/")]
    public void A_valid_app_relative_path_is_accepted(string path)
    {
        foreach (var option in ListOptionNames)
        {
            var services = new ServiceCollection();
            services.AddRazorKit(o => SetList(o, option, new List<string> { path }));
        }
    }

    [Theory]
    [InlineData("Bad\"Name")]
    [InlineData("Bad<Name>")]
    [InlineData("..")]
    [InlineData("/cdn.example")]
    [InlineData("Bad\\Name")]
    [InlineData("Name?x=1")]
    [InlineData("Name#frag")]
    public void A_component_library_name_that_would_derive_an_unsafe_path_stops_startup(string name)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() => services.AddRazorKit(o => o.ComponentLibraryName = name));
        Assert.StartsWith("RazorKit ComponentLibraryName", ex.Message);
    }

    [Fact]
    public void An_unused_component_library_name_is_not_checked()
    {
        var services = new ServiceCollection();

        services.AddRazorKit(o =>
        {
            o.ComponentLibraryName = "Bad\"Name";
            o.PreviewStylesheets = new List<string>();
            o.PreviewScripts = new List<string>();
        });
    }
}
