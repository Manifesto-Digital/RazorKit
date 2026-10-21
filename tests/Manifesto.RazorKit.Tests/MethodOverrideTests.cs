using System.Net;
using Microsoft.AspNetCore.Builder;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// A host may rewrite the request method after routing has chosen RazorKit's POST action
/// (method-override middleware). Request protection must still apply to that action.
/// </summary>
public class MethodOverrideTests
{
    private static HttpRequestMessage CrossSitePost(IEnumerable<KeyValuePair<string, string>> form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PreviewPath) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        return request;
    }

    [Fact]
    public async Task Post_rewritten_to_get_by_a_form_field_is_still_protected()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigurePipeline = app => app.UseHttpMethodOverride(new HttpMethodOverrideOptions { FormFieldName = "_method" })
        });

        var response = await host.Client.SendAsync(CrossSitePost(new KeyValuePair<string, string>[]
        {
            new("_method", "GET"),
            new("propsJson", "{\"Label\":\"Overridden\"}")
        }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("fetch-site", RejectCode(response));
        Assert.DoesNotContain("Overridden", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task Post_rewritten_to_a_safe_method_by_a_header_is_still_protected(string method)
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigurePipeline = app => app.UseHttpMethodOverride()
        });
        var request = CrossSitePost(new KeyValuePair<string, string>[] { new("propsJson", "{\"Label\":\"Overridden\"}") });
        request.Headers.Add("X-HTTP-Method-Override", method);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("fetch-site", RejectCode(response));
        Assert.DoesNotContain("Overridden", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_requests_still_work_with_method_override_enabled()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigurePipeline = app => app.UseHttpMethodOverride()
        });

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(ShellPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(PreviewPath)).StatusCode);
    }
}
