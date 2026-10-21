using System.Net;
using Manifesto.RazorKit.Tests.Support;
using Microsoft.AspNetCore.WebUtilities;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>The RazorKit-owned double-submit token: how the page issues it and how a render POST is checked.</summary>
public class RequestTokenTests
{
    private static string NewTokenValue() =>
        WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task Shell_over_plain_http_sets_a_host_prefixed_strict_cookie()
    {
        await using var host = await TestHost.StartAsync();

        var token = await LoadShellAsync(host.Client);

        var attributes = token.SetCookieHeader.Split(';').Skip(1).Select(a => a.Trim().ToLowerInvariant()).ToArray();
        Assert.Contains("secure", attributes);
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=strict", attributes);
        Assert.Contains("path=/", attributes);
        Assert.Contains("max-age=28800", attributes);
        Assert.DoesNotContain(attributes, a => a.StartsWith("domain", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Issued_value_is_32_random_bytes_and_the_page_carries_the_same_value()
    {
        await using var host = await TestHost.StartAsync();

        var first = await LoadShellAsync(host.Client);
        var second = await LoadShellAsync(host.Client);

        Assert.Equal(32, WebEncoders.Base64UrlDecode(first.CookieValue).Length);
        Assert.Equal(first.CookieValue, first.PageValue);
        Assert.NotEqual(first.CookieValue, second.CookieValue);
    }

    [Fact]
    public async Task A_second_shell_load_reuses_the_cookie_value_and_refreshes_its_lifetime()
    {
        await using var host = await TestHost.StartAsync();
        var first = await LoadShellAsync(host.Client);

        var second = await LoadShellAsync(host.Client, existingCookie: first.CookieValue);

        Assert.Equal(first.CookieValue, second.CookieValue);
        Assert.Equal(first.CookieValue, second.PageValue);
        Assert.Contains("max-age=28800", second.SetCookieHeader.ToLowerInvariant());
    }

    [Theory]
    [InlineData("short")]
    [InlineData("not*base64url*at*all*not*base64url*at*all*x")]
    public async Task A_malformed_cookie_is_replaced_with_a_new_value(string malformed)
    {
        await using var host = await TestHost.StartAsync();

        var token = await LoadShellAsync(host.Client, existingCookie: malformed);

        Assert.NotEqual(malformed, token.CookieValue);
        Assert.Equal(32, WebEncoders.Base64UrlDecode(token.CookieValue).Length);
    }

    [Fact]
    public async Task Matching_cookie_and_field_render()
    {
        await using var host = await TestHost.StartAsync();

        var response = await ValidRenderPostAsync(host.Client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Posted label", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Missing_cookie_is_rejected()
    {
        await using var host = await TestHost.StartAsync();
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(cookie: null, field: token.PageValue));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("token-cookie-missing", RejectCode(response));
    }

    [Fact]
    public async Task Missing_field_is_rejected()
    {
        await using var host = await TestHost.StartAsync();
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, field: null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("token-mismatch", RejectCode(response));
    }

    [Fact]
    public async Task Mismatched_values_are_rejected()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.SendAsync(RenderPost(NewTokenValue(), NewTokenValue()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("token-mismatch", RejectCode(response));
    }

    public static TheoryData<string, string> WrongLengthPairs()
    {
        var shortValue = WebEncoders.Base64UrlEncode(new byte[16]);
        var longValue = WebEncoders.Base64UrlEncode(new byte[48]);
        return new TheoryData<string, string>
        {
            { shortValue, shortValue },
            { longValue, longValue },
            { "", "" },
            { "%%%", "%%%" },
        };
    }

    [Theory]
    [MemberData(nameof(WrongLengthPairs))]
    public async Task Values_that_are_not_32_bytes_are_rejected_even_when_equal(string cookie, string field)
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.SendAsync(RenderPost(cookie, field));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("token-mismatch", RejectCode(response));
    }

    [Fact]
    public async Task A_repeated_token_field_is_rejected()
    {
        await using var host = await TestHost.StartAsync();
        var token = await LoadShellAsync(host.Client);
        var request = new HttpRequestMessage(HttpMethod.Post, PreviewPath)
        {
            Content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
            {
                new("propsJson", "{}"),
                new(TokenField, token.PageValue),
                new(TokenField, token.PageValue),
            })
        };
        request.Headers.Add("Cookie", $"{CookieName}={token.CookieValue}");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("token-mismatch", RejectCode(response));
    }

    [Fact]
    public async Task The_token_is_never_logged()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await TestHost.StartAsync(new TestHostOptions { LoggerProvider = logs });
        var token = await LoadShellAsync(host.Client);

        await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue));
        await host.Client.SendAsync(RenderPost(token.CookieValue, NewTokenValue()));
        await host.Client.SendAsync(RenderPost(cookie: null, token.PageValue));

        Assert.NotEmpty(logs.Entries);
        Assert.Contains(logs.Entries, e => e.Message.Contains("token-mismatch", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, e =>
            e.Message.Contains(token.CookieValue, StringComparison.Ordinal) ||
            e.State.Contains(token.CookieValue, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejections_are_logged_at_warning_with_the_code_and_without_header_values()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await TestHost.StartAsync(new TestHostOptions { LoggerProvider = logs });
        const string origin = "https://unique-origin-value.example";

        await host.Client.SendAsync(RenderPost(null, null, configure: h => h.Add("Origin", origin)));

        var entry = Assert.Single(logs.Entries, e => e.Message.Contains("origin-mismatch", StringComparison.Ordinal));
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, entry.Level);
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains(origin, StringComparison.Ordinal) || e.State.Contains(origin, StringComparison.Ordinal));
    }

    private static HttpRequestMessage PostWithCookieHeader(string cookieHeader, string field)
    {
        var request = RenderPost(cookie: null, field);
        request.Headers.Add("Cookie", cookieHeader);
        return request;
    }

    [Fact]
    public async Task A_repeated_token_cookie_is_rejected()
    {
        await using var host = await TestHost.StartAsync();
        var value = NewTokenValue();

        var response = await host.Client.SendAsync(PostWithCookieHeader($"{CookieName}={value}; {CookieName}={value}", value));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("token-mismatch", RejectCode(response));
    }

    [Fact]
    public async Task A_cookie_whose_name_differs_only_in_case_is_not_the_token_cookie()
    {
        await using var host = await TestHost.StartAsync();
        var value = NewTokenValue();

        var response = await host.Client.SendAsync(PostWithCookieHeader($"{CookieName.ToLowerInvariant()}={value}", value));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("token-cookie-missing", RejectCode(response));
    }

    [Fact]
    public async Task A_differently_cased_cookie_cannot_stand_in_for_the_token_cookie()
    {
        await using var host = await TestHost.StartAsync();
        var real = NewTokenValue();
        var other = NewTokenValue();
        var cookieHeader = $"{CookieName}={real}; {CookieName.ToLowerInvariant()}={other}";

        var withOther = await host.Client.SendAsync(PostWithCookieHeader(cookieHeader, other));
        var withReal = await host.Client.SendAsync(PostWithCookieHeader(cookieHeader, real));

        Assert.Equal(HttpStatusCode.Forbidden, withOther.StatusCode);
        Assert.Equal("token-mismatch", RejectCode(withOther));
        Assert.Equal(HttpStatusCode.OK, withReal.StatusCode);
    }

    [Fact]
    public async Task Shell_does_not_reuse_a_value_from_a_repeated_or_differently_cased_cookie()
    {
        await using var host = await TestHost.StartAsync();
        var value = NewTokenValue();

        using var repeated = new HttpRequestMessage(HttpMethod.Get, ShellPath);
        repeated.Headers.Add("Cookie", $"{CookieName}={value}; {CookieName}={value}");
        using var differentCase = new HttpRequestMessage(HttpMethod.Get, ShellPath);
        differentCase.Headers.Add("Cookie", $"{CookieName.ToLowerInvariant()}={value}");

        foreach (var request in new[] { repeated, differentCase })
        {
            var response = await host.Client.SendAsync(request);
            var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith(CookieName + "=", StringComparison.Ordinal));
            Assert.DoesNotContain(value, setCookie);
        }
    }
}
