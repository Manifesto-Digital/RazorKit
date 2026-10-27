using System.Net;
using System.Net.Http.Headers;
using Manifesto.RazorKit.Filters;
using Manifesto.RazorKit.Tests.Support;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// The origin check on every non-GET RazorKit request. Each case carries a valid token, so the
/// response is decided by the provenance headers alone.
/// </summary>
public class RequestProvenanceTests
{
    private static async Task<HttpResponseMessage> PostWithHeadersAsync(TestHost host, Action<HttpRequestHeaders> headers)
    {
        var token = await LoadShellAsync(host.Client);
        return await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, configure: headers));
    }

    private static async Task AssertRendersAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(RejectCode(response));
        Assert.Contains("<button type=\"button\">Posted label</button>", await response.Content.ReadAsStringAsync());
    }

    private static void AssertRejected(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(code, RejectCode(response));
    }

    [Fact]
    public async Task Same_origin_fetch_metadata_passes()
    {
        await using var host = await TestHost.StartAsync();

        var response = await PostWithHeadersAsync(host, h => h.Add("Sec-Fetch-Site", "same-origin"));

        await AssertRendersAsync(response);
    }

    [Theory]
    [InlineData("same-site")]
    [InlineData("cross-site")]
    [InlineData("none")]
    [InlineData("SAME-ORIGIN")]
    [InlineData("same-origin, cross-site")]
    public async Task Any_other_fetch_metadata_value_is_rejected(string value)
    {
        await using var host = await TestHost.StartAsync();

        var response = await PostWithHeadersAsync(host, h => h.TryAddWithoutValidation("Sec-Fetch-Site", value));

        AssertRejected(response, "fetch-site");
    }

    [Fact]
    public async Task A_repeated_fetch_metadata_header_is_rejected()
    {
        await using var host = await TestHost.StartAsync();

        var response = await PostWithHeadersAsync(host, h => h.TryAddWithoutValidation("Sec-Fetch-Site", new[] { "same-origin", "same-origin" }));

        AssertRejected(response, "fetch-site");
    }

    [Fact]
    public async Task Fetch_metadata_decides_even_when_the_origin_matches()
    {
        await using var host = await TestHost.StartAsync();

        var response = await PostWithHeadersAsync(host, h =>
        {
            h.Add("Sec-Fetch-Site", "cross-site");
            h.Add("Origin", "http://localhost");
        });

        AssertRejected(response, "fetch-site");
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://LOCALHOST")]
    [InlineData("http://localhost:80")]
    public async Task Without_fetch_metadata_an_origin_matching_the_request_passes(string origin)
    {
        await using var host = await TestHost.StartAsync();

        var response = await PostWithHeadersAsync(host, h => h.Add("Origin", origin));

        await AssertRendersAsync(response);
    }

    [Fact]
    public async Task Without_fetch_metadata_a_null_origin_is_rejected()
    {
        await using var host = await TestHost.StartAsync();

        var response = await PostWithHeadersAsync(host, h => h.Add("Origin", "null"));

        AssertRejected(response, "origin-null");
    }

    [Theory]
    [InlineData("https://other.example")]
    [InlineData("http://localhost.other.example")]
    [InlineData("http://localhost:8080")]
    [InlineData("https://localhost")]
    [InlineData("http://localhost/path")]
    [InlineData("localhost")]
    [InlineData("Null")]
    public async Task Without_fetch_metadata_any_other_origin_is_rejected(string origin)
    {
        await using var host = await TestHost.StartAsync();

        var response = await PostWithHeadersAsync(host, h => h.TryAddWithoutValidation("Origin", origin));

        AssertRejected(response, "origin-mismatch");
    }

    [Fact]
    public async Task Without_fetch_metadata_a_repeated_origin_header_is_rejected()
    {
        await using var host = await TestHost.StartAsync();

        var response = await PostWithHeadersAsync(host, h => h.TryAddWithoutValidation("Origin", new[] { "http://localhost", "http://localhost" }));

        AssertRejected(response, "origin-mismatch");
    }

    [Fact]
    public async Task A_request_with_neither_header_is_rejected()
    {
        await using var host = await TestHost.StartAsync();

        var response = await PostWithHeadersAsync(host, _ => { });

        AssertRejected(response, "no-provenance");
    }

    [Fact]
    public async Task Derived_origin_over_plain_http_does_not_match_an_https_origin()
    {
        await using var host = await TestHost.StartAsync();

        var response = await PostWithHeadersAsync(host, h => h.Add("Origin", "https://localhost"));

        AssertRejected(response, "origin-mismatch");
    }

    [Fact]
    public async Task Configured_allowed_origins_accept_the_public_https_origin()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigureRazorKit = o => o.AllowedOrigins.Add("https://localhost")
        });

        var response = await PostWithHeadersAsync(host, h => h.Add("Origin", "https://localhost"));

        await AssertRendersAsync(response);
    }

    [Fact]
    public async Task Configured_allowed_origins_replace_the_derived_origin()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigureRazorKit = o => o.AllowedOrigins.Add("https://www.site.example")
        });

        var response = await PostWithHeadersAsync(host, h => h.Add("Origin", "http://localhost"));

        AssertRejected(response, "origin-mismatch");
    }

    [Fact]
    public async Task Provenance_is_checked_before_the_token()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.SendAsync(RenderPost(cookie: null, field: null, configure: h => h.Add("Sec-Fetch-Site", "cross-site")));

        AssertRejected(response, "fetch-site");
    }

    [Fact]
    public async Task Rejection_body_is_static()
    {
        await using var host = await TestHost.StartAsync();

        var response = await PostWithHeadersAsync(host, h => h.TryAddWithoutValidation("Origin", "https://other.example"));

        Assert.Equal(RejectionPage.Forbidden("origin-mismatch"), await response.Content.ReadAsStringAsync());
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("null")]
    [InlineData("not a url")]
    [InlineData("https://site.example/path")]
    [InlineData("ftp://site.example")]
    public void An_unusable_allowed_origin_fails_at_startup(string origin)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            Extensions.ServiceCollectionExtensions.AddRazorKit(services, o => o.AllowedOrigins.Add(origin)));
    }
}
