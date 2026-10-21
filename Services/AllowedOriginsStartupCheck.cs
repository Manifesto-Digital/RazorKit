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
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AllowedOriginsStartupCheck> _logger;

    public AllowedOriginsStartupCheck(RazorKitOptions options, IHostEnvironment environment, ILogger<AllowedOriginsStartupCheck> logger)
    {
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment() && _options.AllowedOrigins.Count == 0)
        {
            LogAllowedOriginsNotSet(_logger);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "RazorKit AllowedOrigins is not set. Render requests without a Sec-Fetch-Site header are checked against " +
                  "the origin derived from the request's scheme and host, which does not match the public origin behind a " +
                  "proxy or CDN that terminates TLS. Set AllowedOrigins in AddRazorKit to the site's public origin(s).")]
    private static partial void LogAllowedOriginsNotSet(ILogger logger);
}
