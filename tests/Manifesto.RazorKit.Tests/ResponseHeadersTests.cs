using System.Net;
using System.Text;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

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

    [Fact]
    public async Task Request_protection_403_carries_all_three_headers()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.SendAsync(RenderPost(cookie: null, field: null, configure: h => h.Add("Sec-Fetch-Site", "cross-site")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertRazorKitHeaders(response);
    }

    [Fact]
    public async Task Page_405_carries_all_three_headers()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.SendAsync(RenderPost(cookie: null, field: null, path: ShellPath));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        AssertRazorKitHeaders(response);
    }

    [Fact]
    public async Task Body_too_large_413_carries_all_three_headers()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions { ConfigureRazorKit = o => o.MaxPropsJsonBytes = 1024 });
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, new string('a', 1025)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        AssertRazorKitHeaders(response);
    }

    [Fact]
    public async Task Unsupported_media_type_415_carries_all_three_headers()
    {
        await using var host = await TestHost.StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, PreviewPath)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Sec-Fetch-Site", "same-origin");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        AssertRazorKitHeaders(response);
    }
}
