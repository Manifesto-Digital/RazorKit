using System.Net;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

public class PreviewActionTests
{
    [Fact]
    public async Task Get_preview_ignores_props_in_the_query_string()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync(PreviewPath + "?propsJson=" + Uri.EscapeDataString("{\"Label\":\"From query\"}"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<button type=\"button\">Click me</button>", html);
        Assert.DoesNotContain("From query", html);
    }

    [Fact]
    public async Task Get_preview_ignores_props_in_a_form_body()
    {
        await using var host = await TestHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, PreviewPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["propsJson"] = "{\"Label\":\"From body\"}" })
        };

        var response = await host.Client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<button type=\"button\">Click me</button>", html);
        Assert.DoesNotContain("From body", html);
    }

    [Fact]
    public async Task Post_preview_renders_the_posted_props()
    {
        await using var host = await TestHost.StartAsync();

        var response = await ValidRenderPostAsync(host.Client, "{\"Label\":\"Edited\"}");

        Assert.Contains("<button type=\"button\">Edited</button>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_preview_without_a_story_segment_uses_the_default_story()
    {
        await using var host = await TestHost.StartAsync();
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, propsJson: "", path: "/razorkit-preview/preview/button"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<button type=\"button\">Click me</button>", await response.Content.ReadAsStringAsync());
    }
}
