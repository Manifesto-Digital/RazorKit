using Manifesto.RazorKit.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Manifesto.RazorKit.Services;

/// <summary>
/// Warns at startup, outside Development, when AllowedOrigins is not configured. Without it a
/// site behind TLS offload derives an http:// origin and rejects render requests from browsers
/// that send Origin but not Sec-Fetch-Site; the warning makes that visible before anyone reports it.
/// </summary>
internal sealed partial class AllowedOriginsStartupCheck : IHostedService
{
    private readonly RazorKitOptions _options;
    private readonly RazorKitAvailability _availability;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AllowedOriginsStartupCheck> _logger;

    public AllowedOriginsStartupCheck(RazorKitOptions options, RazorKitAvailability availability, IHostEnvironment environment, ILogger<AllowedOriginsStartupCheck> logger)
    {
        _options = options;
        _availability = availability;
        _environment = environment;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Nothing is served, so the origin check can never run and the warning would be noise.
        if (!_availability.IsEnabled)
        {
            LogDisabled(_logger, _availability.EnvironmentName);
            return Task.CompletedTask;
        }

        if (!_environment.IsDevelopment() && _options.AllowedOrigins.Count == 0)
        {
            LogAllowedOriginsNotSet(_logger);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 11, Level = LogLevel.Information,
        Message = "RazorKit is disabled in the '{EnvironmentName}' environment (see EnabledEnvironments). Its routes are not registered.")]
    private static partial void LogDisabled(ILogger logger, string environmentName);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "RazorKit AllowedOrigins is not set. Render requests without a Sec-Fetch-Site header are checked against " +
                  "the origin derived from the request's scheme and host, which does not match the public origin behind a " +
                  "proxy or CDN that terminates TLS. Set AllowedOrigins in AddRazorKit to the site's public origin(s).")]
    private static partial void LogAllowedOriginsNotSet(ILogger logger);
}
