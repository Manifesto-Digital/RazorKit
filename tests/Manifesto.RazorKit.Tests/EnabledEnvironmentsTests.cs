using System.Net;
using Manifesto.RazorKit.Controllers;
using Manifesto.RazorKit.Tests.Support;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

public class EnabledEnvironmentsTests
{
    private const string UnknownPath = "/definitely-not-a-route";

    private static Task<TestHost> StartAsync(string environment, params string[]? enabled) =>
        TestHost.StartAsync(new TestHostOptions
        {
            EnvironmentName = environment,
            ConfigureRazorKit = o =>
            {
                if (enabled is not null)
                {
                    o.EnabledEnvironments = enabled.ToList();
                }
            }
        });

    private static readonly string[] AllRoutes =
    [
        "/razorkit", "/razorkit/", "/RazorKit", "/RAZORKIT/Button/Default", "/razorkit/button", ShellPath,
        "/razorkit-preview/preview/button", PreviewPath, "/RazorKit-Preview/Preview/Button/Default"
    ];

    [Fact]
    public async Task Default_leaves_RazorKit_public_in_Production()
    {
        await using var host = await StartAsync(Environments.Production, enabled: null);

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(ShellPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ValidRenderPostAsync(host.Client)).StatusCode);
    }

    [Fact]
    public async Task Listed_environment_is_served()
    {
        await using var host = await StartAsync(Environments.Staging, "Development", "Staging");

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(ShellPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(PreviewPath)).StatusCode);
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("STAGING")]
    public async Task Environment_names_match_case_insensitively(string listed)
    {
        await using var host = await StartAsync(Environments.Staging, listed);

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(ShellPath)).StatusCode);
    }

    [Theory]
    [InlineData("/razorkit")]
    [InlineData("/razorkit/")]
    [InlineData("//razorkit")]
    [InlineData("/RazorKit")]
    [InlineData("/RAZORKIT/Button/Default")]
    [InlineData("/razorkit/button")]
    [InlineData("/razorkit/button/default")]
    [InlineData("/razorkit-preview/preview/button")]
    [InlineData("/razorkit-preview/preview/button/default")]
    [InlineData("/RazorKit-Preview/Preview/Button/Default")]
    public async Task Unlisted_environment_answers_404_on_every_route(string path)
    {
        await using var host = await StartAsync(Environments.Production, "Development", "Staging");

        var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unlisted_environment_answers_404_to_HEAD_and_POST_before_any_protection()
    {
        await using var host = await StartAsync(Environments.Production, "Development");

        var head = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, PreviewPath));
        var headShell = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, ShellPath));
        // No token and no cookie: request protection would answer 403, so a 404 proves it never ran.
        var post = await host.Client.SendAsync(RenderPost(null, null));
        var withToken = await host.Client.SendAsync(RenderPost("x", "y"));

        Assert.Equal(HttpStatusCode.NotFound, head.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, headShell.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, withToken.StatusCode);
    }

    [Fact]
    public async Task Unlisted_environment_issues_no_cookie_and_spends_no_rate_limit()
    {
        await using var host = await StartAsync(Environments.Production, "Development");

        // Well past the default Page burst of 200: a 429 would mean the bucket was touched.
        for (var i = 0; i < 300; i++)
        {
            var response = await host.Client.GetAsync(ShellPath);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
    }

    [Fact]
    public async Task Disabled_response_matches_an_unknown_path_and_does_not_mention_RazorKit()
    {
        await using var host = await StartAsync(Environments.Production, "Development");

        var unknown = await host.Client.GetAsync(UnknownPath);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        var unknownHeaders = HeaderNames(unknown);

        foreach (var path in AllRoutes)
        {
            var response = await host.Client.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(unknown.StatusCode, response.StatusCode);
            Assert.Equal(unknownBody, body);
            Assert.Equal(unknownHeaders, HeaderNames(response));
            Assert.DoesNotContain(response.Headers, h => h.Key.Contains("RazorKit", StringComparison.OrdinalIgnoreCase));
            Assert.False(response.Headers.Contains("X-Robots-Tag"));
            Assert.False(response.Headers.Contains("Content-Security-Policy"));
            Assert.DoesNotContain("razorkit", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<string> HeaderNames(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers).Select(h => h.Key).Order(StringComparer.OrdinalIgnoreCase).ToList();

    [Fact]
    public async Task Disabling_RazorKit_leaves_the_hosts_own_routes_alone()
    {
        await using var host = await StartAsync(Environments.Production, "Development");

        Assert.Equal(HttpStatusCode.InternalServerError, (await host.Client.GetAsync("/host-error")).StatusCode);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Empty_list_disables_RazorKit_in_every_environment(string environment)
    {
        await using var host = await StartAsync(environment, []);

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(ShellPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(PreviewPath)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_entries_stop_the_host_at_startup(string entry)
    {
        var error = await Assert.ThrowsAnyAsync<ArgumentException>(() => StartAsync(Environments.Production, "Development", entry));

        Assert.Contains("EnabledEnvironments", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"RazorKit":{"EnabledEnvironments":[]}}""")]
    [InlineData("""{"RazorKit":{"EnabledEnvironments":null}}""")]
    [InlineData("""{"RazorKit":{"EnabledEnvironments":""}}""")]
    public void Empty_values_in_JSON_configuration_bind_as_unset_so_RazorKit_stays_enabled(string json)
    {
        // Pins the behaviour the README documents: the configuration binder cannot tell an empty
        // array from a missing key, so "[]" in appsettings means "enabled everywhere".
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();
        var options = new Models.RazorKitOptions();

        configuration.GetSection("RazorKit").Bind(options);

        Assert.Null(options.EnabledEnvironments);
    }

    [Fact]
    public async Task Null_entries_stop_the_host_at_startup()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => TestHost.StartAsync(new TestHostOptions
        {
            ConfigureRazorKit = o => o.EnabledEnvironments = new List<string> { null! }
        }));
    }

    [Fact]
    public async Task Disabled_environment_logs_no_AllowedOrigins_warning_and_one_information_line()
    {
        var logs = new CapturingLoggerProvider();
        await using (await TestHost.StartAsync(new TestHostOptions
        {
            EnvironmentName = Environments.Production,
            LoggerProvider = logs,
            ConfigureRazorKit = o => o.EnabledEnvironments = ["Development"]
        }))
        {
        }

        Assert.DoesNotContain(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("AllowedOrigins", StringComparison.Ordinal));
        var info = Assert.Single(logs.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Production", info.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enabled_environment_without_allowed_origins_still_warns()
    {
        var logs = new CapturingLoggerProvider();
        await using (await TestHost.StartAsync(new TestHostOptions
        {
            EnvironmentName = Environments.Production,
            LoggerProvider = logs,
            ConfigureRazorKit = o => o.EnabledEnvironments = ["Production"]
        }))
        {
        }

        Assert.Single(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("AllowedOrigins", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_RazorKit_endpoint_is_registered_when_disabled()
    {
        await using var host = await StartAsync(Environments.Production, "Development");
        var razorKitAssembly = typeof(RazorKitController).Assembly;

        // Checks the assembly rather than known routes, so an action added later is covered too.
        var endpoints = host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(e => e.Metadata.OfType<ControllerActionDescriptor>().Any(d => d.ControllerTypeInfo.Assembly == razorKitAssembly)
                || e.Metadata.OfType<CompiledPageActionDescriptor>().Any(d => d.ModelTypeInfo?.Assembly == razorKitAssembly))
            .ToList();

        Assert.Empty(endpoints);
    }
}
