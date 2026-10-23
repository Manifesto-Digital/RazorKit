using System.Net;
using Microsoft.AspNetCore.Builder;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

public class BundledAxeTests
{
    private const string AxePath = "/_content/Manifesto.RazorKit/lib/axe-core/axe.min.js";

    private static TestHostOptions WithStaticFiles() => new()
    {
        EnvironmentName = "Development",
        ConfigurePipeline = app => app.UseStaticFiles()
    };

    [Fact]
    public async Task The_preview_loads_the_bundled_axe_not_a_third_party_host()
    {
        await using var host = await TestHost.StartAsync();

        var html = await (await host.Client.GetAsync(PreviewPath)).Content.ReadAsStringAsync();

        Assert.Contains("src=\"" + AxePath + "\"", html);
        Assert.DoesNotContain("unpkg.com", html);
    }

    [Fact]
    public async Task The_explorer_page_does_not_load_axe_from_a_third_party_host()
    {
        await using var host = await TestHost.StartAsync();

        var html = await (await host.Client.GetAsync(ShellPath)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("unpkg.com", html);
    }

    [Fact]
    public async Task The_snippet_embeds_the_component_path_and_name_as_json_string_literals()
    {
        await using var host = await TestHost.StartAsync();

        var html = await (await host.Client.GetAsync(ShellPath)).Content.ReadAsStringAsync();

        // The snippet is written with textContent, so the JS strings must hold raw text, not HTML entities.
        Assert.Contains("Html.PartialAsync(\"' + \"~/Components/Atoms/Button/Button.cshtml\" + '\", new ' + \"Button\" + 'Props'", html);
    }

    [Fact]
    public async Task The_preview_axe_path_follows_the_hosts_path_base()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigurePipeline = app => app.Use((context, next) =>
            {
                context.Request.PathBase = "/site";
                return next();
            })
        });

        var html = await (await host.Client.GetAsync(PreviewPath)).Content.ReadAsStringAsync();

        Assert.Contains("src=\"/site" + AxePath + "\"", html);
    }

    [Fact]
    public async Task The_bundled_axe_is_served_as_javascript()
    {
        await using var host = await TestHost.StartAsync(WithStaticFiles());

        var response = await host.Client.GetAsync(AxePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("/*! axe v", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_axe_licence_is_served_next_to_the_script()
    {
        await using var host = await TestHost.StartAsync(WithStaticFiles());

        var response = await host.Client.GetAsync("/_content/Manifesto.RazorKit/lib/axe-core/LICENSE.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Mozilla Public License", await response.Content.ReadAsStringAsync());
    }
}
