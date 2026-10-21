using Manifesto.RazorKit.Tests.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Manifesto.RazorKit.Tests;

public class StartupWarningTests
{
    private static async Task<IReadOnlyList<CapturedLog>> StartupWarningsAsync(string environment, Action<Models.RazorKitOptions>? configure = null)
    {
        var logs = new CapturingLoggerProvider();
        await using (await TestHost.StartAsync(new TestHostOptions
        {
            EnvironmentName = environment,
            LoggerProvider = logs,
            ConfigureRazorKit = configure
        }))
        {
        }

        return logs.Entries
            .Where(e => e.Level == LogLevel.Warning && e.Message.Contains("AllowedOrigins", StringComparison.Ordinal))
            .ToList();
    }

    [Fact]
    public async Task Production_without_allowed_origins_logs_a_warning()
    {
        var warnings = await StartupWarningsAsync(Environments.Production);

        var warning = Assert.Single(warnings);
        Assert.StartsWith("Manifesto.RazorKit", warning.Category);
    }

    [Fact]
    public async Task Production_with_allowed_origins_logs_no_warning()
    {
        Assert.Empty(await StartupWarningsAsync(Environments.Production, o => o.AllowedOrigins.Add("https://www.site.example")));
    }

    [Fact]
    public async Task Development_without_allowed_origins_logs_no_warning()
    {
        Assert.Empty(await StartupWarningsAsync(Environments.Development));
    }
}
