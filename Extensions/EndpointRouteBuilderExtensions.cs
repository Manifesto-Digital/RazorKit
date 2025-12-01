using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Manifesto.RazorKit.Extensions;

/// <summary>
/// Extension methods for configuring RazorKit routing
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps RazorKit routes (controller and Razor Pages)
    /// </summary>
    public static IEndpointRouteBuilder MapRazorKit(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapControllers();
        endpoints.MapRazorPages();
        return endpoints;
    }
}
