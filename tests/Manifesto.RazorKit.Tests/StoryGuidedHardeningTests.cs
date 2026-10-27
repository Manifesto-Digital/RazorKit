using System.Diagnostics;
using System.Net;
using Manifesto.RazorKit.Tests.Support;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// Cost and failure bounds of the Controls re-render's story-guided rebuild: a story value is
/// server code, but a cyclic graph, an endless sequence or an unserialisable member must still
/// not hang a request or take the explorer page down.
/// </summary>
public class StoryGuidedHardeningTests
{
    private static async Task<(HttpResponseMessage Response, string Html)> PostProbeAsync(TestHost host, string story, string propsJson)
    {
        var token = await LoadShellAsync(host.Client);
        var path = $"/razorkit-preview/preview/probe/{story}";
        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, propsJson, path: path));
        return (response, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_cyclic_branching_story_graph_renders_within_a_tight_bound()
    {
        await using var host = await TestHost.StartAsync();

        var clock = Stopwatch.StartNew();
        var task = PostProbeAsync(host, "Cyclic", "{\"Root\":{\"Name\":\"posted\",\"Children\":[{\"Name\":\"a\"},{\"Name\":\"b\"}]}}");
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        clock.Stop();

        Assert.Same(task, finished);
        var (response, html) = await task;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-root=\"posted\"", html);
        Assert.Contains("data-children=\"2\"", html);
    }

    [Fact]
    public async Task A_lazy_endless_story_collection_does_not_hang_the_post()
    {
        await using var host = await TestHost.StartAsync();

        var task = PostProbeAsync(host, "Infinite", "{\"Links\":[{\"Text\":\"one\"},{\"Text\":\"two\"}]}");
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(task, finished);
        var (response, html) = await task;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-links=\"2\"", html);
    }

    [Fact]
    public async Task A_string_posted_into_a_guided_slot_keeps_the_storys_value()
    {
        await using var host = await TestHost.StartAsync();

        var (response, html) = await PostProbeAsync(host, "Guided", "{\"Action\":\"not an object\"}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-action=\"Story action\"", html);
    }

    [Fact]
    public async Task A_story_value_with_an_unserialisable_member_does_not_break_the_explorer_page()
    {
        await using var host = await TestHost.StartAsync();

        foreach (var story in new[] { "Unserialisable", "Cyclic" })
        {
            var response = await host.Client.GetAsync($"/razorkit/probe/{story}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("<form id=\"component-form\"", await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("NullGetter")]
    [InlineData("ThrowingGetter")]
    public async Task A_story_value_with_a_throwing_getter_does_not_break_the_explorer_page(string story)
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync($"/razorkit/probe/{story}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<form id=\"component-form\"", html);
        Assert.Contains("Still here", html);
    }
}
