using System.Net;

namespace Manifesto.RazorKit.Tests;

public class MutationIsolationTests
{
    private static async Task<string> RenderAsync(TestHost host)
    {
        var response = await host.Client.GetAsync("/razorkit-preview/preview/mutating/default");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task A_view_that_mutates_its_list_prop_does_not_change_the_next_requests_output()
    {
        await using var host = await TestHost.StartAsync();

        var first = await RenderAsync(host);
        var second = await RenderAsync(host);
        var third = await RenderAsync(host);

        Assert.Contains("items:2", first);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
    }

    [Fact]
    public async Task Concurrent_requests_do_not_share_story_state()
    {
        // GET previews take a render slot, and the default cap scales with the CPU count, so on a
        // small CI runner some of these 20 parallel requests would get 503. This test is about
        // story isolation, not the cap, so give every request a slot.
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigureRazorKit = o => o.RateLimit.MaxConcurrentRenders = 64,
        });

        var outputs = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => RenderAsync(host)));

        Assert.All(outputs, html => Assert.Contains("items:2", html));
    }
}
