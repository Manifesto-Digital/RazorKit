using System.Net;
using System.Text;
using Manifesto.RazorKit.Filters;
using Manifesto.RazorKit.Tests.Components.Atoms.Waiting;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// Every rejection RazorKit answers itself is a small fixed HTML page, so a blocked preview reads
/// as a message in the iframe rather than raw text. Status codes and headers are pinned by the
/// filter tests; these tests pin the body: HTML, readable, and built only from static strings.
/// </summary>
[Collection(WaitingGateCollection.Name)]
public class RejectionPageTests
{
    // Request-supplied text that must never appear in any rejection body.
    private const string Marker = "rkmarker7Q";

    private static TestHostOptions Limits(int renderBurst = 1000, int pageBurst = 1000, int maxConcurrent = 100) =>
        new()
        {
            ConfigureRazorKit = o =>
            {
                o.MaxPropsJsonBytes = 1024;
                o.RateLimit.Render.PermitsPerMinute = 1;
                o.RateLimit.Render.Burst = renderBurst;
                o.RateLimit.Page.PermitsPerMinute = 1;
                o.RateLimit.Page.Burst = pageBurst;
                o.RateLimit.MaxConcurrentRenders = maxConcurrent;
            }
        };

    private static async Task<string> AssertRejectionPageAsync(HttpResponseMessage response, string headline)
    {
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<!DOCTYPE html>", body);
        Assert.Contains("<html lang=\"en\">", body);
        Assert.Contains($"<title>{headline}</title>", body);
        Assert.Contains($"<h1>{headline}</h1>", body);
        Assert.DoesNotContain(Marker, body);
        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
        return body;
    }

    public static TheoryData<string> AllRejectCodes => new()
    {
        RequestProtectionFilter.RejectCodes.FetchSite,
        RequestProtectionFilter.RejectCodes.NoProvenance,
        RequestProtectionFilter.RejectCodes.OriginNull,
        RequestProtectionFilter.RejectCodes.OriginMismatch,
        RequestProtectionFilter.RejectCodes.TokenCookieMissing,
        RequestProtectionFilter.RejectCodes.TokenMismatch
    };

    [Theory]
    [MemberData(nameof(AllRejectCodes))]
    public void Each_reject_code_has_its_own_page_naming_the_code(string code)
    {
        var page = RejectionPage.Forbidden(code);

        Assert.Contains($"<h1>Preview blocked ({code})</h1>", page);
        Assert.Contains("Reload the page", page);
    }

    [Fact]
    public void An_unknown_code_gets_a_generic_page_that_does_not_repeat_it()
    {
        var page = RejectionPage.Forbidden("<b>" + Marker);

        Assert.Contains("<h1>Preview blocked</h1>", page);
        Assert.DoesNotContain(Marker, page);
        Assert.DoesNotContain("<b>", page);
    }

    [Fact]
    public async Task A_403_is_an_html_page_naming_the_reject_code_and_echoing_nothing()
    {
        await using var host = await TestHost.StartAsync();
        using var request = RenderPost(MintToken(), Marker, propsJson: "{\"Label\":\"" + Marker + "\"}", configure: h =>
        {
            h.Add("Sec-Fetch-Site", "cross-site");
            h.Add("Origin", "https://" + Marker + ".example");
            h.Add("Referer", "https://" + Marker + ".example/");
        });
        request.RequestUri = new Uri(PreviewPath + "?q=" + Marker, UriKind.Relative);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("fetch-site", RejectCode(response));
        await AssertRejectionPageAsync(response, "Preview blocked (fetch-site)");
    }

    [Fact]
    public async Task A_403_for_a_token_mismatch_names_that_code()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.SendAsync(RenderPost(MintToken(), MintToken()));

        Assert.Equal("token-mismatch", RejectCode(response));
        await AssertRejectionPageAsync(response, "Preview blocked (token-mismatch)");
    }

    [Fact]
    public async Task A_413_is_an_html_page()
    {
        await using var host = await TestHost.StartAsync(Limits());
        var token = MintToken();

        var response = await host.Client.SendAsync(RenderPost(token, token, propsJson: "{\"Label\":\"" + new string('a', 4096) + Marker + "\"}"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await AssertRejectionPageAsync(response, "Request too large");
    }

    [Fact]
    public async Task A_415_is_an_html_page()
    {
        await using var host = await TestHost.StartAsync();
        var token = MintToken();
        using var request = RenderPost(token, token);
        request.Content = new StringContent("{\"Label\":\"" + Marker + "\"}", Encoding.UTF8, "application/json");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        await AssertRejectionPageAsync(response, "Unsupported request format");
    }

    [Fact]
    public async Task A_405_on_the_explorer_page_is_an_html_page()
    {
        await using var host = await TestHost.StartAsync();
        var token = MintToken();

        var response = await host.Client.SendAsync(RenderPost(token, token, path: ShellPath));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        await AssertRejectionPageAsync(response, "Method not allowed");
    }

    [Fact]
    public async Task A_429_is_an_html_page_and_keeps_Retry_After()
    {
        await using var host = await TestHost.StartAsync(Limits(renderBurst: 1));
        var token = MintToken();

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(RenderPost(token, token))).StatusCode);
        var response = await host.Client.SendAsync(RenderPost(token, token));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Single(response.Headers.GetValues("Retry-After"));
        var body = await AssertRejectionPageAsync(response, "Too many requests");
        Assert.Contains("Try again in a moment.", body);
    }

    [Fact]
    public async Task A_503_is_an_html_page_and_keeps_Retry_After()
    {
        await using var host = await TestHost.StartAsync(Limits(maxConcurrent: 1));
        WaitingGate.Arm();
        try
        {
            var token = MintToken();
            var held = host.Client.SendAsync(RenderPost(token, token, path: "/razorkit-preview/preview/waiting/default"));
            await WaitingGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            var response = await host.Client.SendAsync(RenderPost(token, token));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("1", Assert.Single(response.Headers.GetValues("Retry-After")));
            await AssertRejectionPageAsync(response, "RazorKit is busy");

            WaitingGate.Release();
            Assert.Equal(HttpStatusCode.OK, (await held).StatusCode);
        }
        finally
        {
            WaitingGate.Release();
        }
    }
}
