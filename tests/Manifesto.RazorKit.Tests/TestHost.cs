using Manifesto.RazorKit.Extensions;
using Manifesto.RazorKit.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Manifesto.RazorKit.Tests;

public sealed record TestHostOptions
{
    /// <summary>False reproduces a host that maps RazorKit but never calls AddRazorKit.</summary>
    public bool AddRazorKit { get; init; } = true;

    /// <summary>Renders unhandled exceptions through the host's own UseExceptionHandler endpoint.</summary>
    public bool UseExceptionHandler { get; init; }

    /// <summary>A Content-Security-Policy the host's own middleware sets before RazorKit runs.</summary>
    public string? HostContentSecurityPolicy { get; init; }

    /// <summary>Applied after the fixture's own RazorKit settings.</summary>
    public Action<RazorKitOptions>? ConfigureRazorKit { get; init; }

    public string EnvironmentName { get; init; } = Environments.Production;

    /// <summary>Receives every log entry the host writes, at every level.</summary>
    public ILoggerProvider? LoggerProvider { get; init; }

    /// <summary>Host service registrations, applied after AddRazorKit.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    /// <summary>Host middleware, added before the endpoints are mapped.</summary>
    public Action<WebApplication>? ConfigurePipeline { get; init; }

    /// <summary>Extra endpoint mapping, after the host's MapControllers and MapRazorPages.</summary>
    public Action<WebApplication>? MapEndpoints { get; init; }
}

/// <summary>
/// An in-memory host wired the way consumers wire RazorKit today:
/// AddControllers, AddRazorPages, AddRazorKit, MapControllers, MapRazorPages.
/// Runs over plain HTTP, like an origin behind TLS offload.
/// </summary>
public sealed class TestHost : IAsyncDisposable
{
    public const string HostErrorBody = "host error page";

    private TestHost(WebApplication app, HttpClient client)
    {
        App = app;
        Client = client;
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public static async Task<TestHost> StartAsync(TestHostOptions? options = null)
    {
        options ??= new TestHostOptions();
        var testAssembly = typeof(TestHost).Assembly;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = testAssembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = options.EnvironmentName
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (options.LoggerProvider is { } loggerProvider)
        {
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.Logging.AddProvider(loggerProvider);
        }

        builder.Services.AddControllers();
        builder.Services.AddRazorPages();
        if (options.AddRazorKit)
        {
            builder.Services.AddRazorKit(o =>
            {
                o.ComponentLibraryName = "Fixture.Components";
                options.ConfigureRazorKit?.Invoke(o);
            });
        }

        options.ConfigureServices?.Invoke(builder.Services);

        var app = builder.Build();

        if (options.UseExceptionHandler)
        {
            app.UseExceptionHandler("/host-error");
        }

        if (options.HostContentSecurityPolicy is { } csp)
        {
            app.Use(async (context, next) =>
            {
                context.Response.Headers.ContentSecurityPolicy = csp;
                await next();
            });
        }

        options.ConfigurePipeline?.Invoke(app);

        app.MapControllers();
        app.MapRazorPages();
        options.MapEndpoints?.Invoke(app);
        app.Map("/host-error", async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync(HostErrorBody);
        });

        await app.StartAsync();

        // The raw handler does not follow redirects or keep cookies, so 302s can be asserted
        // and each test decides exactly which cookies a request carries.
        var client = new HttpClient(app.GetTestServer().CreateHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        return new TestHost(app, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
    }
}
