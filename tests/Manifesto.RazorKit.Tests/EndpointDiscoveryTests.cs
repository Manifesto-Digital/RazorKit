using Manifesto.RazorKit.Controllers;
using Manifesto.RazorKit.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
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
    public async Task Every_RazorKit_action_and_page_opts_out_of_host_antiforgery_validation()
    {
        await using var host = await TestHost.StartAsync();

        var descriptors = await RazorKitDescriptorsAsync(host);

        Assert.NotEmpty(descriptors);
        foreach (var descriptor in descriptors)
        {
            Assert.Contains(descriptor.FilterDescriptors, f => f.Filter is IgnoreAntiforgeryTokenAttribute);
        }
    }

    // A page accepts every method, so every page is a non-GET path. An action is one unless its
    // HTTP method constraint limits it to GET or HEAD.
    // This reads declared metadata only and cannot see a host rewriting the method at runtime;
    // MethodOverrideTests is the tripwire for that.
    private static bool AcceptsNonGetMethods(ActionDescriptor descriptor)
    {
        if (descriptor is CompiledPageActionDescriptor)
        {
            return true;
        }

        var methods = descriptor.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods).ToList();
        return methods is null || methods.Count == 0 || methods.Any(m => !HttpMethods.IsGet(m) && !HttpMethods.IsHead(m));
    }

    [Fact]
    public async Task Every_non_get_RazorKit_path_carries_request_protection()
    {
        await using var host = await TestHost.StartAsync();

        var nonGet = (await RazorKitDescriptorsAsync(host)).Where(AcceptsNonGetMethods).ToList();

        Assert.Contains(nonGet, d => d is ControllerActionDescriptor);
        Assert.Contains(nonGet, d => d is CompiledPageActionDescriptor);
        foreach (var descriptor in nonGet)
        {
            Assert.Contains(descriptor.FilterDescriptors, f => f.Filter is RequestProtectionAttribute);
        }
    }

    [Fact]
    public async Task Pages_carry_the_method_check()
    {
        await using var host = await TestHost.StartAsync();

        var pages = (await RazorKitDescriptorsAsync(host)).OfType<CompiledPageActionDescriptor>().ToList();

        Assert.NotEmpty(pages);
        Assert.All(pages, p => Assert.Contains(p.FilterDescriptors, f => f.Filter is MethodCheckAttribute));
    }

    [Fact]
    public async Task RazorKit_filters_run_in_the_documented_order()
    {
        await using var host = await TestHost.StartAsync();

        foreach (var descriptor in await RazorKitDescriptorsAsync(host))
        {
            // MVC sorts filters by Order, then by scope; the sorted list is the run order.
            var razorKitFilters = descriptor.FilterDescriptors
                .OrderBy(f => f.Order).ThenBy(f => f.Scope)
                .Select(f => f.Filter)
                .Where(f => f is ResponseHeadersAttribute or MethodCheckAttribute or RequestProtectionAttribute)
                .Select(f => f.GetType())
                .ToList();
            var expected = new[] { typeof(ResponseHeadersAttribute), typeof(MethodCheckAttribute), typeof(RequestProtectionAttribute) }
                .Where(razorKitFilters.Contains)
                .ToList();

            Assert.Equal(expected, razorKitFilters);
            Assert.True(RazorKitFilterOrder.ResponseHeaders < RazorKitFilterOrder.MethodCheck);
            Assert.True(RazorKitFilterOrder.MethodCheck < RazorKitFilterOrder.RequestProtection);
            Assert.All(descriptor.FilterDescriptors.Where(f => f.Filter is ResponseHeadersAttribute or MethodCheckAttribute or RequestProtectionAttribute),
                f => Assert.True(f.Order < 0, "RazorKit filters must order ahead of MVC's built-in filters."));
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

        static bool IsRazorKitFilter(object f) => f is ResponseHeadersAttribute or MethodCheckAttribute or RequestProtectionAttribute;
        Assert.DoesNotContain(mvcOptions.Filters, f =>
            IsRazorKitFilter(f) ||
            (f is Microsoft.AspNetCore.Mvc.Filters.FilterDescriptor fd && IsRazorKitFilter(fd.Filter)));
    }
}
