using System.Net;
using Manifesto.RazorKit.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

public class EndpointMappingTests
{
    [Fact]
    public async Task Mapping_controllers_and_pages_repeatedly_yields_one_endpoint_per_RazorKit_route()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            MapEndpoints = app =>
            {
                app.MapControllers();
                app.MapRazorPages();
                app.MapRazorKit();
            }
        });

        var endpoints = host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimStart('/').StartsWith("razorkit", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var keys = endpoints.Select(e =>
            e.RoutePattern.RawText + " " + string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>()))
            .ToList();
        Assert.NotEmpty(keys);
        Assert.Equal(keys.Distinct().Count(), keys.Count);

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(ShellPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ValidRenderPostAsync(host.Client)).StatusCode);
    }

    [Fact]
    public async Task Controllers_mapped_again_on_a_route_group_still_reject_a_cross_site_post()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            MapEndpoints = app => app.MapGroup("/extra").MapControllers()
        });
        var token = await LoadShellAsync(host.Client);

        var groupPreview = await host.Client.GetAsync("/extra" + PreviewPath);
        var crossSite = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue,
            configure: h => h.Add("Sec-Fetch-Site", "cross-site"), path: "/extra" + PreviewPath));

        Assert.Equal(HttpStatusCode.OK, groupPreview.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossSite.StatusCode);
        Assert.Equal("fetch-site", RejectCode(crossSite));
    }
}
