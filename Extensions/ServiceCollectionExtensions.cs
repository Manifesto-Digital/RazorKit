using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Manifesto.RazorKit.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the RazorKit services to the DI container
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Action to configure RazorKit options</param>
    public static IServiceCollection AddRazorKit(this IServiceCollection services, Action<RazorKitOptions> configure)
    {
        var options = new RazorKitOptions();
        configure(options);
        services.AddSingleton(options);

        services.AddScoped<ComponentDiscoveryService>();
        services.AddScoped<ComponentPropertyService>();
        services.AddScoped<ComponentStoryService>();

        return services;
    }
}
