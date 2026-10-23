using Manifesto.RazorKit.Filters;
using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Registry;
using Manifesto.RazorKit.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        ValidateRateLimits(options.RateLimit);

        services.AddSingleton(options);

        // Parsed here so a malformed AllowedOrigins entry stops the host at startup rather than
        // surfacing on the first render request.
        services.AddSingleton(AllowedOriginList.Create(options.AllowedOrigins));

        services.AddSingleton<ResponseHeadersFilter>();
        services.AddSingleton<MethodCheckFilter>();
        services.AddSingleton<RequestProtectionFilter>();
        services.AddHostedService<AllowedOriginsStartupCheck>();

        // Singletons: the limits are per process, so every request must see the same buckets.
        services.AddSingleton<RazorKitRateLimiter>();
        services.AddSingleton<RateLimitFilter>();
        services.AddSingleton<ConcurrencyLimitFilter>();

        // Singleton: scans the loaded assemblies once, lazily, on the first request that needs it.
        services.AddSingleton(sp => new RazorKitRegistry(sp.GetRequiredService<ILogger<RazorKitRegistry>>()));

        services.AddScoped<ComponentDiscoveryService>();
        services.AddScoped<ComponentPropertyService>();
        services.AddScoped<ComponentStoryService>();

        return services;
    }

    // Fails at startup: a zero or negative limit would otherwise surface as an error on every request.
    private static void ValidateRateLimits(RazorKitRateLimitOptions limits)
    {
        foreach (var (name, bucket) in new[] { (nameof(limits.Page), limits.Page), (nameof(limits.Render), limits.Render) })
        {
            if (bucket.PermitsPerMinute < 1 || bucket.Burst < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(limits), $"RazorKit rate limit '{name}' needs PermitsPerMinute and Burst of at least 1.");
            }
        }

        if (limits.MaxConcurrentRenders < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "RazorKit MaxConcurrentRenders must be at least 1.");
        }
    }
}
