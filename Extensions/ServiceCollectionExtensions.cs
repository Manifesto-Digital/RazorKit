using Manifesto.RazorKit.Filters;
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

        if (options.MaxPropsJsonBytes is <= 0 or > RazorKitOptions.MaxMaxPropsJsonBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(configure),
                $"RazorKit MaxPropsJsonBytes must be between 1 and {RazorKitOptions.MaxMaxPropsJsonBytes}.");
        }

        services.AddSingleton(options);

        // Parsed here so a malformed AllowedOrigins entry stops the host at startup rather than
        // surfacing on the first render request.
        services.AddSingleton(AllowedOriginList.Create(options.AllowedOrigins));

        services.AddSingleton<ResponseHeadersFilter>();
        services.AddSingleton<MethodCheckFilter>();
        services.AddSingleton<RequestProtectionFilter>();
        services.AddHostedService<AllowedOriginsStartupCheck>();

        services.AddScoped<ComponentDiscoveryService>();
        services.AddScoped<ComponentPropertyService>();
        services.AddScoped<ComponentStoryService>();

        return services;
    }
}
