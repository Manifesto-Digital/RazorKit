using System.Net;

namespace Manifesto.RazorKit.Tests;

public class ResponseHeadersTests
{
    private const string EnforcedCsp = "object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'self'";

    private static void AssertRazorKitHeaders(HttpResponseMessage response)
    {
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Contains(EnforcedCsp, response.Headers.GetValues("Content-Security-Policy"));
    }

    [Fact]
    public async Task Page_200_carries_all_three_headers()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync("/razorkit/button/default");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertRazorKitHeaders(response);
    }

    [Fact]
    public async Task Page_redirect_carries_all_three_headers()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync("/razorkit");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        AssertRazorKitHeaders(response);
    }

    [Fact]
    public async Task Preview_200_carries_all_three_headers()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync("/razorkit-preview/preview/button/default");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertRazorKitHeaders(response);
    }

    [Fact]
    public async Task Failure_rendered_by_the_host_exception_handler_still_carries_all_three_headers()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions { UseExceptionHandler = true });

        var response = await host.Client.GetAsync("/razorkit/exploding/default");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(TestHost.HostErrorBody, await response.Content.ReadAsStringAsync());
        AssertRazorKitHeaders(response);
    }

    [Theory]
    [InlineData("/razorkit/button/default")]
    [InlineData("/razorkit-preview/preview/button/default")]
    public async Task Csp_is_appended_to_a_policy_the_host_already_set(string path)
    {
        const string hostPolicy = "default-src 'self'";
        await using var host = await TestHost.StartAsync(new TestHostOptions { HostContentSecurityPolicy = hostPolicy });

        var response = await host.Client.GetAsync(path);

        var policies = response.Headers.GetValues("Content-Security-Policy").ToArray();
        Assert.Equal(new[] { hostPolicy, EnforcedCsp }, policies);
    }
}
