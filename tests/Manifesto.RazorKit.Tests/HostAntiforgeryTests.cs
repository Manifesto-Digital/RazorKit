using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// RazorKit's page and preview must work whatever the host does with its own antiforgery
/// configuration, and must never call the host's IAntiforgery.
/// </summary>
public class HostAntiforgeryTests
{
    private static TestHostOptions StrictAntiforgeryHost(bool throwingAntiforgery) => new()
    {
        ConfigureServices = services =>
        {
            services.AddAntiforgery(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
            services.Configure<MvcOptions>(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
            if (throwingAntiforgery)
            {
                services.AddSingleton<IAntiforgery, ThrowingAntiforgery>();
            }
        },
        ConfigurePipeline = app => app.UseAntiforgery()
    };

    [Fact]
    public async Task Shell_and_render_post_succeed_under_a_strict_host_antiforgery_policy_over_plain_http()
    {
        await using var host = await TestHost.StartAsync(StrictAntiforgeryHost(throwingAntiforgery: false));

        var shell = await host.Client.GetAsync(ShellPath);
        var render = await ValidRenderPostAsync(host.Client);

        Assert.Equal(HttpStatusCode.OK, shell.StatusCode);
        Assert.Equal(HttpStatusCode.OK, render.StatusCode);
        Assert.Contains("Posted label", await render.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_host_antiforgery_service_is_never_called()
    {
        await using var host = await TestHost.StartAsync(StrictAntiforgeryHost(throwingAntiforgery: true));

        var shell = await host.Client.GetAsync(ShellPath);
        var render = await ValidRenderPostAsync(host.Client);
        var rejected = await host.Client.SendAsync(RenderPost(cookie: null, field: null));
        var pagePost = await host.Client.SendAsync(RenderPost(cookie: null, field: null, path: ShellPath));

        Assert.Equal(HttpStatusCode.OK, shell.StatusCode);
        Assert.Equal(HttpStatusCode.OK, render.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, pagePost.StatusCode);
    }

    private sealed class ThrowingAntiforgery : IAntiforgery
    {
        private static Exception Called() => new InvalidOperationException("The host IAntiforgery was called.");

        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => throw Called();

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => throw Called();

        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => throw Called();

        public Task ValidateRequestAsync(HttpContext httpContext) => throw Called();

        public void SetCookieTokenAndHeader(HttpContext httpContext) => throw Called();
    }
}
