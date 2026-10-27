using System.Net;
using System.Text;
using Manifesto.RazorKit.Filters;
using Manifesto.RazorKit.Tests.Support;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>The request body cap, content-type allowlist and JSON depth limit on render POSTs.</summary>
public class BodyLimitTests
{
    private const int Cap = 1024;

    private static Task<TestHost> StartAsync() =>
        TestHost.StartAsync(new TestHostOptions { ConfigureRazorKit = o => o.MaxPropsJsonBytes = Cap });

    private static string LabelOfLength(int totalLength)
    {
        const string prefix = "{\"Label\":\"";
        const string suffix = "\"}";
        return prefix + new string('a', totalLength - prefix.Length - suffix.Length) + suffix;
    }

    private static async Task AssertTooLargeAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(RejectionPage.TooLarge, await response.Content.ReadAsStringAsync());
    }

    private static HttpRequestMessage PostContent(IssuedToken token, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PreviewPath) { Content = content };
        request.Headers.Add("Cookie", $"{CookieName}={token.CookieValue}");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        return request;
    }

    [Fact]
    public async Task Props_at_the_cap_render()
    {
        await using var host = await StartAsync();
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, LabelOfLength(Cap)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Props_one_byte_over_the_cap_are_rejected()
    {
        await using var host = await StartAsync();
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, LabelOfLength(Cap + 1)));

        await AssertTooLargeAsync(response);
    }

    [Fact]
    public async Task The_cap_counts_utf8_bytes_not_characters()
    {
        await using var host = await StartAsync();
        var token = await LoadShellAsync(host.Client);
        var props = "{\"Label\":\"" + new string('€', 400) + "\"}";

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, props));

        await AssertTooLargeAsync(response);
    }

    [Fact]
    public async Task A_declared_content_length_over_the_body_cap_is_rejected()
    {
        await using var host = await StartAsync();
        var token = await LoadShellAsync(host.Client);
        var body = $"{TokenField}={token.PageValue}&propsJson=" + new string('a', Cap * 8);

        var response = await host.Client.SendAsync(PostContent(token,
            new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")));

        await AssertTooLargeAsync(response);
    }

    [Fact]
    public async Task A_body_without_content_length_is_rejected_once_it_passes_the_cap()
    {
        await using var host = await StartAsync();
        var token = await LoadShellAsync(host.Client);
        var body = $"{TokenField}={token.PageValue}&propsJson=" + new string('a', Cap * 8);

        var response = await host.Client.SendAsync(PostContent(token,
            new UnknownLengthContent(body, "application/x-www-form-urlencoded")));

        await AssertTooLargeAsync(response);
    }

    [Fact]
    public async Task A_body_without_content_length_is_rejected_once_it_passes_the_cap_even_when_each_value_is_within_limits()
    {
        await using var host = await StartAsync();
        var token = await LoadShellAsync(host.Client);
        var filler = string.Concat(Enumerable.Range(0, 6).Select(i => $"&f{i}=" + new string('a', Cap * 2)));
        var body = $"{TokenField}={token.PageValue}&propsJson=%7B%7D" + filler;

        var response = await host.Client.SendAsync(PostContent(token,
            new UnknownLengthContent(body, "application/x-www-form-urlencoded")));

        await AssertTooLargeAsync(response);
    }

    [Fact]
    public async Task More_than_eight_form_values_are_rejected()
    {
        await using var host = await StartAsync();
        var token = await LoadShellAsync(host.Client);
        var body = $"{TokenField}={token.PageValue}&propsJson=%7B%7D" + string.Concat(Enumerable.Range(0, 7).Select(i => $"&f{i}=x"));

        var response = await host.Client.SendAsync(PostContent(token,
            new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")));

        await AssertTooLargeAsync(response);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData("application/x-www-form-urlencoded-extra")]
    public async Task Any_content_type_other_than_url_encoded_form_is_rejected(string contentType)
    {
        await using var host = await StartAsync();
        var token = await LoadShellAsync(host.Client);
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes($"{TokenField}={token.PageValue}&propsJson=%7B%7D"));
        content.Headers.TryAddWithoutValidation("Content-Type", contentType);

        var response = await host.Client.SendAsync(PostContent(token, content));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(RejectionPage.UnsupportedMediaType, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_post_with_no_content_type_is_rejected()
    {
        await using var host = await StartAsync();
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(PostContent(token, new ByteArrayContent(Array.Empty<byte>())));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Url_encoded_form_with_a_charset_parameter_is_accepted()
    {
        await using var host = await StartAsync();
        var token = await LoadShellAsync(host.Client);
        var body = $"{TokenField}={token.PageValue}&propsJson=" + Uri.EscapeDataString("{\"Label\":\"Charset\"}");

        var response = await host.Client.SendAsync(PostContent(token,
            new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Charset", await response.Content.ReadAsStringAsync());
    }

    private static string Nested(int depth) =>
        "{\"Label\":\"Deep\",\"extra\":" + new string('[', depth - 1) + new string(']', depth - 1) + "}";

    [Fact]
    public async Task Props_nested_sixteen_deep_render()
    {
        await using var host = await StartAsync();

        var response = await ValidRenderPostAsync(host.Client, Nested(16));

        Assert.Contains("<button type=\"button\">Deep</button>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Props_nested_seventeen_deep_are_not_rendered()
    {
        await using var host = await StartAsync();

        var response = await ValidRenderPostAsync(host.Client, Nested(17));

        Assert.DoesNotContain("<button type=\"button\">Deep</button>", await response.Content.ReadAsStringAsync());
    }
}

public class BodyLimitPaddingTests
{
    [Fact]
    public async Task A_body_without_content_length_padded_with_empty_pairs_is_rejected_once_it_passes_the_cap()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions { ConfigureRazorKit = o => o.MaxPropsJsonBytes = 1024 });
        var token = await LoadShellAsync(host.Client);
        var body = $"{TokenField}={token.PageValue}&propsJson=%7B%7D" + new string('&', 1024 * 8);
        var request = new HttpRequestMessage(HttpMethod.Post, PreviewPath)
        {
            Content = new UnknownLengthContent(body, "application/x-www-form-urlencoded")
        };
        request.Headers.Add("Cookie", $"{CookieName}={token.CookieValue}");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
}

public class BodyLimitAfterHostReadTests
{
    // A host middleware that reads every form before routing reaches RazorKit, under the
    // framework's default limits rather than RazorKit's.
    private static Task<TestHost> StartAsync() => TestHost.StartAsync(new TestHostOptions
    {
        ConfigureRazorKit = o => o.MaxPropsJsonBytes = 1024,
        ConfigurePipeline = app => app.Use(async (context, next) =>
        {
            if (context.Request.HasFormContentType)
            {
                await context.Request.ReadFormAsync();
            }

            await next();
        })
    });

    [Fact]
    public async Task Limits_still_apply_when_the_host_read_the_form_first()
    {
        await using var host = await StartAsync();
        var token = await LoadShellAsync(host.Client);

        var oversized = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, new string('a', 1025)));
        var tooMany = new HttpRequestMessage(HttpMethod.Post, PreviewPath)
        {
            Content = new FormUrlEncodedContent(Enumerable.Range(0, 8)
                .Select(i => new KeyValuePair<string, string>($"f{i}", "x"))
                .Append(new(TokenField, token.PageValue)))
        };
        tooMany.Headers.Add("Cookie", $"{CookieName}={token.CookieValue}");
        tooMany.Headers.Add("Sec-Fetch-Site", "same-origin");
        var tooManyResponse = await host.Client.SendAsync(tooMany);
        var valid = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooManyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }
}
