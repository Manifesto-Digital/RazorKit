using Manifesto.RazorKit.Extensions;
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
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddControllers();
        builder.Services.AddRazorPages();
        if (options.AddRazorKit)
        {
            builder.Services.AddRazorKit(o => o.ComponentLibraryName = "Fixture.Components");
        }

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

        app.MapControllers();
        app.MapRazorPages();
        app.Map("/host-error", async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync(HostErrorBody);
        });

        await app.StartAsync();

        // The raw handler does not follow redirects, so 302s can be asserted.
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
