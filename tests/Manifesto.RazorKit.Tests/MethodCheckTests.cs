using System.Net;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

public class MethodCheckTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task Explorer_page_answers_anything_but_get_and_head_with_405(string method)
    {
        await using var host = await TestHost.StartAsync();
        var token = await LoadShellAsync(host.Client);
        using var request = RenderPost(token.CookieValue, token.PageValue, path: ShellPath);
        request.Method = new HttpMethod(method);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(new[] { "GET", "HEAD" }, response.Content.Headers.Allow.OrderBy(m => m));
        Assert.Equal("Method not allowed.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Explorer_page_answers_head()
    {
        await using var host = await TestHost.StartAsync();

        var response = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, ShellPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
