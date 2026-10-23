using System.Net;

namespace Manifesto.RazorKit.Tests;

public class PageModelSmokeTests
{
    [Fact]
    public async Task Root_redirects_to_the_first_story_of_the_first_component()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync("/razorkit");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/razorkit/button/default", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Component_without_a_story_redirects_to_its_first_story()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync("/razorkit/button");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/razorkit/button/default", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Story_page_renders_the_explorer_with_the_preview_iframe_url()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync("/razorkit/button/default");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/razorkit-preview/preview/button/default", html);
        Assert.Contains("Button / Default", html);
    }

    [Fact]
    public async Task Preview_renders_the_fixture_component()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync("/razorkit-preview/preview/button/default");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<button type=\"button\">Click me</button>", html);
    }
}
