using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace Manifesto.RazorKit.Tests.Support;

/// <summary>A token pair as the explorer page issues it: the cookie value and the value the page script sends back.</summary>
public sealed record IssuedToken(string CookieValue, string PageValue, string SetCookieHeader);

/// <summary>Builds requests the way the explorer page's script does, so each test varies one thing.</summary>
public static partial class RazorKitRequests
{
    public const string CookieName = "__Host-RazorKit-AF";
    public const string TokenField = "__RazorKitToken";
    public const string RejectHeader = "X-RazorKit-Reject";
    public const string ShellPath = "/razorkit/button/default";
    public const string PreviewPath = "/razorkit-preview/preview/button/default";

    [GeneratedRegex("<meta name=\"razorkit-token\" content=\"([^\"]*)\"")]
    private static partial Regex MetaToken();

    public static async Task<IssuedToken> LoadShellAsync(HttpClient client, string? existingCookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ShellPath);
        if (existingCookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={existingCookie}");
        }

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith(CookieName + "=", StringComparison.Ordinal));
        var cookieValue = setCookie[(CookieName.Length + 1)..].Split(';')[0];
        var html = await response.Content.ReadAsStringAsync();
        var match = MetaToken().Match(html);
        Assert.True(match.Success, "The page did not render the token meta element.");
        return new IssuedToken(cookieValue, WebUtility.HtmlDecode(match.Groups[1].Value), setCookie);
    }

    /// <summary>
    /// A render POST with a valid token and Sec-Fetch-Site: same-origin. Pass null for the cookie
    /// or the field to leave it out; use <paramref name="configure"/> to change headers.
    /// </summary>
    public static HttpRequestMessage RenderPost(
        string? cookie,
        string? field,
        string propsJson = "{\"Label\":\"Posted label\"}",
        Action<HttpRequestHeaders>? configure = null,
        string path = PreviewPath)
    {
        var form = new List<KeyValuePair<string, string>> { new("propsJson", propsJson) };
        if (field is not null)
        {
            form.Add(new(TokenField, field));
        }

        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) };
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={cookie}");
        }

        if (configure is null)
        {
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
        }
        else
        {
            configure(request.Headers);
        }

        return request;
    }

    public static async Task<HttpResponseMessage> ValidRenderPostAsync(HttpClient client, string propsJson = "{\"Label\":\"Posted label\"}")
    {
        var token = await LoadShellAsync(client);
        return await client.SendAsync(RenderPost(token.CookieValue, token.PageValue, propsJson));
    }

    /// <summary>
    /// A well-formed token value minted by the test. The token is double-submit, so any 32-byte
    /// value sent as both the cookie and the field passes; minting one locally lets a test make
    /// render POSTs without a shell GET, which would spend the page bucket.
    /// </summary>
    public static string MintToken() =>
        Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Makes <paramref name="request"/> pass request protection: same-origin provenance and a
    /// matching token cookie and field. <paramref name="form"/> is the rest of the form body.
    /// </summary>
    public static HttpRequestMessage WithValidProtection(HttpRequestMessage request, IEnumerable<KeyValuePair<string, string>>? form = null)
    {
        var token = MintToken();
        var fields = new List<KeyValuePair<string, string>>(form ?? Array.Empty<KeyValuePair<string, string>>())
        {
            new(TokenField, token)
        };
        request.Content = new FormUrlEncodedContent(fields);
        request.Headers.Add("Cookie", $"{CookieName}={token}");
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        return request;
    }

    public static string? RejectCode(HttpResponseMessage response) =>
        response.Headers.TryGetValues(RejectHeader, out var values) ? values.Single() : null;

    /// <summary>A form body sent without a Content-Length, as a chunked upload would arrive.</summary>
    public sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _body;

        public UnknownLengthContent(string body, string contentType)
        {
            _body = Encoding.UTF8.GetBytes(body);
            Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_body, 0, _body.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
