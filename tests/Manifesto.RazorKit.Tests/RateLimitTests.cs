using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Manifesto.RazorKit.Tests.Components.Atoms.Waiting;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

[Collection(WaitingGateCollection.Name)]
public class RateLimitTests
{
    private const string Preview = "/razorkit-preview/preview/button/default";
    private const string Shell = "/razorkit/button/default";

    // One permit a minute, so a bucket never refills during a test and its burst is the exact allowance.
    private static TestHostOptions Limits(int renderBurst = 1000, int pageBurst = 1000, int maxConcurrent = 100) =>
        new()
        {
            ConfigureRazorKit = o =>
            {
                o.RateLimit.Render.PermitsPerMinute = 1;
                o.RateLimit.Render.Burst = renderBurst;
                o.RateLimit.Page.PermitsPerMinute = 1;
                o.RateLimit.Page.Burst = pageBurst;
                o.RateLimit.MaxConcurrentRenders = maxConcurrent;
            }
        };

    // Passes request protection, so what these tests observe is the limit and not a 403.
    private static HttpRequestMessage Post(string ip, string path = Preview)
    {
        var request = WithValidProtection(new HttpRequestMessage(HttpMethod.Post, path));
        request.Headers.Add(TestHost.RemoteIpHeader, ip);
        return request;
    }

    private static HttpRequestMessage Get(string ip, string path = Shell)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestHost.RemoteIpHeader, ip);
        return request;
    }

    private static void AssertRetryAfter(HttpResponseMessage response, int? exactly = null)
    {
        var seconds = Assert.Single(response.Headers.GetValues("Retry-After"));
        var parsed = int.Parse(seconds);
        Assert.True(parsed >= 1, $"Retry-After was {seconds}");
        if (exactly is { } expected)
        {
            Assert.Equal(expected, parsed);
        }
    }

    [Fact]
    public async Task Render_bucket_returns_429_with_Retry_After_once_the_burst_is_spent()
    {
        await using var host = await TestHost.StartAsync(Limits(renderBurst: 2));

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Post("203.0.113.1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Post("203.0.113.1"))).StatusCode);
        var limited = await host.Client.SendAsync(Post("203.0.113.1"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        AssertRetryAfter(limited);
        Assert.DoesNotContain("<button", await limited.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_different_address_is_not_affected_by_another_clients_limit()
    {
        await using var host = await TestHost.StartAsync(Limits(renderBurst: 1));

        await host.Client.SendAsync(Post("203.0.113.1"));
        var limited = await host.Client.SendAsync(Post("203.0.113.1"));
        var other = await host.Client.SendAsync(Post("203.0.113.2"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Forwarded_headers_never_change_the_partition()
    {
        await using var host = await TestHost.StartAsync(Limits(renderBurst: 1));

        async Task<HttpStatusCode> PostSpoofed(string spoofed)
        {
            var request = Post("203.0.113.1");
            request.Headers.Add("X-Forwarded-For", spoofed);
            request.Headers.Add("CF-Connecting-IP", spoofed);
            request.Headers.Add("X-Real-IP", spoofed);
            return (await host.Client.SendAsync(request)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await PostSpoofed("198.51.100.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostSpoofed("198.51.100.2"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostSpoofed("198.51.100.3"));
    }

    [Fact]
    public async Task Page_bucket_limits_GET_requests_independently_of_the_render_bucket()
    {
        await using var host = await TestHost.StartAsync(Limits(pageBurst: 2, renderBurst: 5));

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("203.0.113.1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("203.0.113.1", Preview))).StatusCode);
        var limited = await host.Client.SendAsync(Get("203.0.113.1"));
        var render = await host.Client.SendAsync(Post("203.0.113.1"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        AssertRetryAfter(limited);
        Assert.Equal(HttpStatusCode.OK, render.StatusCode);
    }

    [Fact]
    public async Task Ipv6_addresses_in_one_slash_64_share_a_partition()
    {
        await using var host = await TestHost.StartAsync(Limits(renderBurst: 1));

        var first = await host.Client.SendAsync(Post("2001:db8:1:2::1"));
        var sameNetwork = await host.Client.SendAsync(Post("2001:db8:1:2:ffff:ffff:ffff:ffff"));
        var otherNetwork = await host.Client.SendAsync(Post("2001:db8:1:3::1"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, sameNetwork.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherNetwork.StatusCode);
    }

    [Fact]
    public async Task Ipv4_mapped_ipv6_shares_a_partition_with_the_plain_ipv4_address()
    {
        await using var host = await TestHost.StartAsync(Limits(renderBurst: 1));

        var plain = await host.Client.SendAsync(Post("203.0.113.9"));
        var mapped = await host.Client.SendAsync(Post("::ffff:203.0.113.9"));

        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, mapped.StatusCode);
    }

    [Fact]
    public async Task A_missing_remote_address_is_limited_not_exempt()
    {
        await using var host = await TestHost.StartAsync(Limits(renderBurst: 1));

        var first = await host.Client.SendAsync(Post("null"));
        var second = await host.Client.SendAsync(Post("null"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task Limits_apply_with_no_host_rate_limiter()
    {
        // TestHost registers no AddRateLimiter/UseRateLimiter unless asked to.
        await using var host = await TestHost.StartAsync(Limits(renderBurst: 1));

        await host.Client.SendAsync(Post("203.0.113.1"));
        var limited = await host.Client.SendAsync(Post("203.0.113.1"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task A_host_rate_limiter_neither_replaces_nor_is_replaced_by_the_RazorKit_limit()
    {
        var options = Limits(renderBurst: 1) with { HostRateLimiter = true };
        await using var host = await TestHost.StartAsync(options);

        var first = await host.Client.SendAsync(Post("203.0.113.1"));
        var limited = await host.Client.SendAsync(Post("203.0.113.1"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotEqual(TestHost.HostRateLimiterRejection, (int)limited.StatusCode);
    }

    [Fact]
    public async Task Concurrency_backstop_returns_503_with_Retry_After_1_when_saturated()
    {
        await using var host = await TestHost.StartAsync(Limits(maxConcurrent: 1));
        WaitingGate.Arm();
        try
        {
            var held = host.Client.SendAsync(Post("203.0.113.1", "/razorkit-preview/preview/waiting/default"));
            await WaitingGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            var rejected = await host.Client.SendAsync(Post("203.0.113.2"));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
            AssertRetryAfter(rejected, exactly: 1);

            WaitingGate.Release();
            Assert.Equal(HttpStatusCode.OK, (await held).StatusCode);
        }
        finally
        {
            WaitingGate.Release();
        }
    }

    // Both override styles rewrite Request.Method after routing has chosen the POST action.
    public static TheoryData<string> OverrideStyles => new() { "form-field", "header" };

    private static TestHostOptions WithOverride(TestHostOptions options, string style) =>
        options with
        {
            HttpMethodOverride = style == "form-field"
                ? new HttpMethodOverrideOptions { FormFieldName = "_method" }
                : new HttpMethodOverrideOptions()
        };

    private static HttpRequestMessage OverriddenPost(string style, string ip, string path = Preview)
    {
        var form = style == "form-field"
            ? new[] { new KeyValuePair<string, string>("_method", "GET") }
            : Array.Empty<KeyValuePair<string, string>>();
        var request = WithValidProtection(new HttpRequestMessage(HttpMethod.Post, path), form);
        if (style == "header")
        {
            request.Headers.Add("X-HTTP-Method-Override", "GET");
        }

        request.Headers.Add(TestHost.RemoteIpHeader, ip);
        return request;
    }

    [Theory]
    [MemberData(nameof(OverrideStyles))]
    public async Task A_POST_overridden_to_GET_is_still_charged_to_the_render_bucket(string style)
    {
        await using var host = await TestHost.StartAsync(WithOverride(Limits(renderBurst: 2), style));

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(OverriddenPost(style, "203.0.113.1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(OverriddenPost(style, "203.0.113.1"))).StatusCode);
        var limited = await host.Client.SendAsync(OverriddenPost(style, "203.0.113.1"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Theory]
    [MemberData(nameof(OverrideStyles))]
    public async Task A_POST_overridden_to_GET_takes_a_concurrency_slot(string style)
    {
        await using var host = await TestHost.StartAsync(WithOverride(Limits(maxConcurrent: 1), style));
        WaitingGate.Arm();
        try
        {
            var held = host.Client.SendAsync(OverriddenPost(style, "203.0.113.1", "/razorkit-preview/preview/waiting/default"));
            await WaitingGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            var rejected = await host.Client.SendAsync(OverriddenPost(style, "203.0.113.2"));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);

            WaitingGate.Release();
            Assert.Equal(HttpStatusCode.OK, (await held).StatusCode);
        }
        finally
        {
            WaitingGate.Release();
        }
    }

    [Fact]
    public async Task Concurrency_slot_is_released_when_the_render_finishes()
    {
        await using var host = await TestHost.StartAsync(Limits(maxConcurrent: 1));

        for (var i = 0; i < 3; i++)
        {
            var response = await host.Client.SendAsync(Post("203.0.113.1"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task GET_requests_do_not_take_a_render_slot()
    {
        await using var host = await TestHost.StartAsync(Limits(maxConcurrent: 1));
        WaitingGate.Arm();
        try
        {
            var held = host.Client.SendAsync(Post("203.0.113.1", "/razorkit-preview/preview/waiting/default"));
            await WaitingGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            var page = await host.Client.SendAsync(Get("203.0.113.2"));

            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            WaitingGate.Release();
            await held;
        }
        finally
        {
            WaitingGate.Release();
        }
    }

    private const string WaitingPreview = "/razorkit-preview/preview/waiting/default";

    [Fact]
    public async Task A_GET_preview_gets_503_with_Retry_After_1_when_every_render_slot_is_held()
    {
        await using var host = await TestHost.StartAsync(Limits(maxConcurrent: 1));
        WaitingGate.Arm();
        try
        {
            var held = host.Client.SendAsync(Post("203.0.113.1", WaitingPreview));
            await WaitingGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            var rejected = await host.Client.SendAsync(Get("203.0.113.2", Preview));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
            AssertRetryAfter(rejected, exactly: 1);

            WaitingGate.Release();
            Assert.Equal(HttpStatusCode.OK, (await held).StatusCode);
        }
        finally
        {
            WaitingGate.Release();
        }
    }

    [Fact]
    public async Task A_held_GET_preview_holds_the_slot_against_a_POST_preview()
    {
        await using var host = await TestHost.StartAsync(Limits(maxConcurrent: 1));
        WaitingGate.Arm();
        try
        {
            var held = host.Client.SendAsync(Get("203.0.113.1", WaitingPreview));
            await WaitingGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            var rejected = await host.Client.SendAsync(Post("203.0.113.2"));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);

            WaitingGate.Release();
            Assert.Equal(HttpStatusCode.OK, (await held).StatusCode);
        }
        finally
        {
            WaitingGate.Release();
        }
    }

    [Fact]
    public async Task A_GET_preview_releases_its_slot_when_it_finishes()
    {
        await using var host = await TestHost.StartAsync(Limits(maxConcurrent: 1));

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("203.0.113.1", Preview))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Post("203.0.113.1"))).StatusCode);
    }

    [Fact]
    public async Task The_page_shell_does_not_take_a_slot_while_a_GET_preview_is_held()
    {
        await using var host = await TestHost.StartAsync(Limits(maxConcurrent: 1));
        WaitingGate.Arm();
        try
        {
            var held = host.Client.SendAsync(Get("203.0.113.1", WaitingPreview));
            await WaitingGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("203.0.113.2"))).StatusCode);

            WaitingGate.Release();
            await held;
        }
        finally
        {
            WaitingGate.Release();
        }
    }

    [Fact]
    public async Task A_GET_preview_is_charged_to_the_page_bucket_and_not_the_render_bucket()
    {
        await using var host = await TestHost.StartAsync(Limits(renderBurst: 1, pageBurst: 2));

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("203.0.113.1", Preview))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Get("203.0.113.1", Preview))).StatusCode);

        // The page bucket (burst 2) is spent by the two previews; the render bucket is untouched.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.Client.SendAsync(Get("203.0.113.1", Preview))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Post("203.0.113.1"))).StatusCode);
    }

    [Fact]
    public async Task A_GET_preview_limited_by_the_page_bucket_is_told_429_not_503_while_slots_are_held()
    {
        await using var host = await TestHost.StartAsync(Limits(pageBurst: 1, maxConcurrent: 1));
        WaitingGate.Arm();
        try
        {
            var held = host.Client.SendAsync(Get("203.0.113.1", WaitingPreview));
            await WaitingGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            var limited = await host.Client.SendAsync(Get("203.0.113.1", Preview));

            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            WaitingGate.Release();
            await held;
        }
        finally
        {
            WaitingGate.Release();
        }
    }

    [Fact]
    public async Task A_request_limited_by_the_token_bucket_never_takes_a_render_slot()
    {
        await using var host = await TestHost.StartAsync(Limits(renderBurst: 1, maxConcurrent: 1));
        WaitingGate.Arm();
        try
        {
            // Spends this client's only token and holds the only slot.
            var held = host.Client.SendAsync(Post("203.0.113.1", "/razorkit-preview/preview/waiting/default"));
            await WaitingGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            // Out of tokens, so it is told 429 (its own fault), not 503 (capacity).
            var limited = await host.Client.SendAsync(Post("203.0.113.1"));

            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            WaitingGate.Release();
            await held;
        }
        finally
        {
            WaitingGate.Release();
        }
    }

    [Fact]
    public async Task Concurrency_slot_is_released_when_the_response_starts_not_when_the_body_has_been_read()
    {
        await using var host = await TestHost.StartAsync(Limits(maxConcurrent: 1));

        // The body is far larger than any buffer, so it cannot finish until this client reads it.
        using var slowReader = await host.Client.SendAsync(
            Post("203.0.113.1", "/razorkit-preview/preview/large/default"),
            HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, slowReader.StatusCode);

        var second = await host.Client.SendAsync(Post("203.0.113.2"));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
}
