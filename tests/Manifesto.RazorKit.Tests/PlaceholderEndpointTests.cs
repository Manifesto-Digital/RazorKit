using System.Net;
using System.Text;
using System.Xml.Linq;
using Manifesto.RazorKit.Tests.Components.Atoms.Waiting;

namespace Manifesto.RazorKit.Tests;

[Collection(WaitingGateCollection.Name)]
public class PlaceholderEndpointTests
{
    private const string Base = "/razorkit-preview/placeholder";

    private static async Task<(HttpResponseMessage Response, string Body)> GetAsync(TestHost host, string path)
    {
        var response = await host.Client.GetAsync(path);
        return (response, await response.Content.ReadAsStringAsync());
    }

    // Well-formedness is the injection check that matters: smuggled markup either breaks the
    // parse or shows up as an element or attribute the generator never writes.
    private static XElement Parse(string svg)
    {
        var root = XDocument.Parse(svg).Root!;
        var allowedElements = new[] { "svg", "title", "rect", "text" };
        Assert.All(root.DescendantsAndSelf(), e => Assert.Contains(e.Name.LocalName, allowedElements));
        Assert.All(
            root.DescendantsAndSelf().SelectMany(e => e.Attributes()),
            a => Assert.False(a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase), a.Name.LocalName));
        return root;
    }

    private static XElement Child(XElement svg, string name) => svg.Elements().First(e => e.Name.LocalName == name);

    [Fact]
    public async Task Valid_size_returns_an_svg_of_that_size_with_the_default_label()
    {
        await using var host = await TestHost.StartAsync();

        var (response, body) = await GetAsync(host, $"{Base}/640x360");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        var svg = Parse(body);
        Assert.Equal("640", svg.Attribute("width")?.Value);
        Assert.Equal("360", svg.Attribute("height")?.Value);
        Assert.Equal("0 0 640 360", svg.Attribute("viewBox")?.Value);
        Assert.Equal("img", svg.Attribute("role")?.Value);
        Assert.Equal("640×360", svg.Attribute("aria-label")?.Value);
        Assert.Equal("640×360", Child(svg, "title").Value);
    }

    [Theory]
    [InlineData("1x1", 16, 16)]
    [InlineData("0x0", 16, 16)]
    [InlineData("15x4000", 16, 4000)]
    [InlineData("4001x99999", 4000, 4000)]
    [InlineData("20000x20", 4000, 20)]
    public async Task Sizes_are_clamped_to_the_allowed_range(string size, int width, int height)
    {
        await using var host = await TestHost.StartAsync();

        var (response, body) = await GetAsync(host, $"{Base}/{size}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var svg = Parse(body);
        Assert.Equal(width.ToString(), svg.Attribute("width")?.Value);
        Assert.Equal(height.ToString(), svg.Attribute("height")?.Value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("100")]
    [InlineData("100x")]
    [InlineData("x100")]
    [InlineData("axb")]
    [InlineData("-5x10")]
    [InlineData("10x-5")]
    [InlineData("1.5x10")]
    [InlineData("100x100x100")]
    [InlineData("1000000x10")]
    [InlineData("99999999999999999999x10")]
    [InlineData("%3Cscript%3Ex10")]
    public async Task Malformed_size_is_a_404_with_no_svg(string size)
    {
        await using var host = await TestHost.StartAsync();

        var (response, body) = await GetAsync(host, $"{Base}/{size}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("<svg", body);
    }

    [Fact]
    public async Task Safe_label_is_shown_in_the_text_title_and_aria_label()
    {
        await using var host = await TestHost.StartAsync();

        var (response, body) = await GetAsync(host, $"{Base}/300x200?label=Hero%20image%20(1)%3A%20a-b_c.d%2Ce");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var svg = Parse(body);
        const string expected = "Hero image (1): a-b_c.d,e";
        Assert.Equal(expected, svg.Attribute("aria-label")?.Value);
        Assert.Equal(expected, Child(svg, "text").Value);
        Assert.Equal(expected, Child(svg, "title").Value);
    }

    [Fact]
    public async Task Unicode_letters_are_allowed_and_survive_intact()
    {
        await using var host = await TestHost.StartAsync();

        var (response, body) = await GetAsync(host, $"{Base}/300x200?label=" + Uri.EscapeDataString("Café 日本"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Café 日本", Parse(body).Attribute("aria-label")?.Value);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("\"><svg onload=alert(1)>")]
    [InlineData("a&b")]
    [InlineData("a&amp;b")]
    [InlineData("it's")]
    [InlineData("a/b")]
    [InlineData("]]>")]
    [InlineData("a\nb")]
    [InlineData("‮reversed")]
    [InlineData("&#60;script&#62;")]
    public async Task Label_outside_the_safe_charset_is_a_400_and_never_reaches_the_output(string label)
    {
        await using var host = await TestHost.StartAsync();

        var (response, body) = await GetAsync(host, $"{Base}/300x200?label={Uri.EscapeDataString(label)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("<svg", body);
        Assert.DoesNotContain("onload", body);
    }

    [Fact]
    public async Task Label_over_the_length_limit_is_a_400()
    {
        await using var host = await TestHost.StartAsync();

        var (ok, _) = await GetAsync(host, $"{Base}/300x200?label={new string('a', 40)}");
        var (tooLong, _) = await GetAsync(host, $"{Base}/300x200?label={new string('a', 41)}");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Blank_label_falls_back_to_the_default()
    {
        await using var host = await TestHost.StartAsync();

        var (response, body) = await GetAsync(host, $"{Base}/300x200?label=%20%20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("300×200", Parse(body).Attribute("aria-label")?.Value);
    }

    [Theory]
    [InlineData("bg=336699", "#336699")]
    [InlineData("bg=369", "#336699")]
    [InlineData("bg=%23336699", "#336699")]
    [InlineData("bg=ABCDEF", "#abcdef")]
    public async Task Valid_background_colours_are_used_lower_cased_and_expanded(string query, string expected)
    {
        await using var host = await TestHost.StartAsync();

        var (_, body) = await GetAsync(host, $"{Base}/100x100?{query}");

        Assert.Equal(expected, Child(Parse(body), "rect").Attribute("fill")?.Value);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("12")]
    [InlineData("1234")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("gggggg")]
    [InlineData("%23%23333")]
    [InlineData("url(javascript:alert(1))")]
    [InlineData("333;}</style><script>")]
    [InlineData("%22%20onload%3D%22x")]
    public async Task Invalid_colours_fall_back_to_the_defaults_without_failing(string value)
    {
        await using var host = await TestHost.StartAsync();

        var (plainResponse, plainBody) = await GetAsync(host, $"{Base}/100x100");
        var (response, body) = await GetAsync(host, $"{Base}/100x100?bg={value}&fg={value}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, plainResponse.StatusCode);
        Assert.Equal(plainBody, body);
        Parse(body);
    }

    [Fact]
    public async Task Foreground_colour_is_applied_to_the_text()
    {
        await using var host = await TestHost.StartAsync();

        var (_, body) = await GetAsync(host, $"{Base}/100x100?bg=000&fg=ff0");

        Assert.Equal("#ffff00", Child(Parse(body), "text").Attribute("fill")?.Value);
    }

    [Fact]
    public async Task Text_colour_defaults_to_a_readable_contrast_against_the_background()
    {
        await using var host = await TestHost.StartAsync();

        var (_, dark) = await GetAsync(host, $"{Base}/100x100?bg=000");
        var (_, light) = await GetAsync(host, $"{Base}/100x100?bg=fff");

        Assert.NotEqual(
            Child(Parse(dark), "text").Attribute("fill")?.Value,
            Child(Parse(light), "text").Attribute("fill")?.Value);
    }

    [Fact]
    public async Task Success_response_is_cacheable_for_a_year_and_carries_the_hardening_headers()
    {
        await using var host = await TestHost.StartAsync();

        var (response, _) = await GetAsync(host, $"{Base}/100x100");

        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl!.Private);
        Assert.Equal(TimeSpan.FromDays(365), cacheControl.MaxAge);
        Assert.Contains("immutable", cacheControl.ToString());
        Assert.False(cacheControl.NoStore);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        var policies = response.Headers.GetValues("Content-Security-Policy").ToList();
        Assert.Contains("object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'self'", policies);
        Assert.Contains("default-src 'none'; style-src 'unsafe-inline'", policies);
    }

    [Fact]
    public async Task A_host_policy_stays_enforced_alongside_the_placeholder_policies()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions { HostContentSecurityPolicy = "img-src 'self'" });

        var (response, _) = await GetAsync(host, $"{Base}/100x100");

        Assert.Contains("img-src 'self'", response.Headers.GetValues("Content-Security-Policy"));
    }

    [Theory]
    [InlineData("/razorkit-preview/placeholder/abc")]
    [InlineData("/razorkit-preview/placeholder/100x100?label=%3Cb%3E")]
    public async Task Error_responses_are_never_cached_by_the_long_lived_header(string path)
    {
        await using var host = await TestHost.StartAsync();

        var (response, _) = await GetAsync(host, path);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
    }

    // Web cache deception: a host page that re-executes an error as a 200 must not inherit the
    // image's long-lived header, or a shared cache could keep one user's page for a year.
    [Theory]
    [InlineData("/razorkit-preview/placeholder/abc")]
    [InlineData("/razorkit-preview/placeholder/100x100?label=%3Cx")]
    public async Task A_host_page_re_executed_as_a_200_never_gets_the_cache_header(string path)
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigurePipeline = app => app.UseStatusCodePagesWithReExecute("/soft404"),
            MapEndpoints = app => app.Map("/soft404", async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await context.Response.WriteAsync("personalised host page");
            })
        });

        var (response, body) = await GetAsync(host, path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("personalised host page", body);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Success_response_is_private_not_public()
    {
        await using var host = await TestHost.StartAsync();

        var (response, _) = await GetAsync(host, $"{Base}/100x100");

        Assert.True(response.Headers.CacheControl?.Private);
        Assert.False(response.Headers.CacheControl?.Public);
        Assert.DoesNotContain("public", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Same_url_gives_byte_identical_output()
    {
        await using var host = await TestHost.StartAsync();

        var (_, first) = await GetAsync(host, $"{Base}/321x123?label=Same&bg=abc");
        var (_, second) = await GetAsync(host, $"{Base}/321x123?label=Same&bg=abc");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Rate_limit_still_applies_and_the_429_is_not_cached()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigureRazorKit = o =>
            {
                o.RateLimit.Page.PermitsPerMinute = 1;
                o.RateLimit.Page.Burst = 2;
            }
        });

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync($"{Base}/100x100")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync($"{Base}/100x100")).StatusCode);
        var limited = await host.Client.GetAsync($"{Base}/100x100");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal("no-store", limited.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Placeholder_requests_do_not_take_a_render_slot()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigureRazorKit = o => o.RateLimit.MaxConcurrentRenders = 1
        });

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => host.Client.GetAsync($"{Base}/100x100")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task Methods_other_than_GET_and_HEAD_are_rejected(string method)
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"{Base}/100x100")
        {
            Content = new StringContent("x", Encoding.UTF8, "application/x-www-form-urlencoded")
        });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.NotEqual("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    // The body is not asserted: the test server, unlike Kestrel, does not strip it from a HEAD response.
    public async Task Head_succeeds_with_the_image_headers()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"{Base}/100x100"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.Private);
    }

    [Fact]
    public async Task A_component_named_placeholder_is_still_reachable_in_the_explorer()
    {
        await using var host = await TestHost.StartAsync();

        var shell = await host.Client.GetAsync("/razorkit/placeholder/default");
        var preview = await host.Client.GetAsync("/razorkit-preview/preview/placeholder/default");

        Assert.Equal(HttpStatusCode.OK, shell.StatusCode);
        Assert.Equal("text/html", shell.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", shell.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("text/html", preview.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Existing_endpoints_keep_no_store()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.GetAsync("/razorkit-preview/preview/button/default");

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }
}
