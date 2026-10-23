using Manifesto.RazorKit.Controllers;
using Manifesto.RazorKit.Filters;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Manifesto.RazorKit.Tests;

public class EndpointDiscoveryTests
{
    // Selects by the assembly that owns the controller or compiled page type, so a page
    // RazorKit adds later is covered without touching this test.
    private static async Task<List<ActionDescriptor>> RazorKitDescriptorsAsync(TestHost host)
    {
        var provider = host.App.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var pageLoader = host.App.Services.GetRequiredService<PageLoader>();
        var razorKitAssembly = typeof(RazorKitController).Assembly;
        var result = new List<ActionDescriptor>();

        foreach (var descriptor in provider.ActionDescriptors.Items)
        {
            switch (descriptor)
            {
                case ControllerActionDescriptor controller
                    when controller.ControllerTypeInfo.Assembly == razorKitAssembly:
                    result.Add(controller);
                    break;
                case PageActionDescriptor page:
                    var compiled = await pageLoader.LoadAsync(page, EndpointMetadataCollection.Empty);
                    if (compiled.PageTypeInfo.Assembly == razorKitAssembly)
                    {
                        result.Add(compiled);
                    }
                    break;
            }
        }

        return result;
    }

    [Fact]
    public async Task Every_RazorKit_action_and_page_carries_the_response_headers_filter()
    {
        await using var host = await TestHost.StartAsync();

        var descriptors = await RazorKitDescriptorsAsync(host);

        // Guards against the enumeration silently matching nothing.
        Assert.Contains(descriptors, d => d is ControllerActionDescriptor);
        Assert.Contains(descriptors, d => d is CompiledPageActionDescriptor);
        foreach (var descriptor in descriptors)
        {
            Assert.Contains(descriptor.FilterDescriptors, f => f.Filter is ResponseHeadersAttribute);
        }
    }

    [Fact]
    public async Task Every_RazorKit_action_is_attribute_routed()
    {
        await using var host = await TestHost.StartAsync();

        var actions = (await RazorKitDescriptorsAsync(host)).OfType<ControllerActionDescriptor>().ToList();

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotNull(a.AttributeRouteInfo));
    }

    [Fact]
    public async Task No_RazorKit_filter_is_registered_globally()
    {
        await using var host = await TestHost.StartAsync();

        var mvcOptions = host.App.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Mvc.MvcOptions>>().Value;

        Assert.DoesNotContain(mvcOptions.Filters, f =>
            f is ResponseHeadersAttribute ||
            (f is Microsoft.AspNetCore.Mvc.Filters.FilterDescriptor fd && fd.Filter is ResponseHeadersAttribute));
    }
}
