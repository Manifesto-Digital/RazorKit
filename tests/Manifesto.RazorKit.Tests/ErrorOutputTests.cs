using System.Net;
using Manifesto.RazorKit.Tests.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

public class ErrorOutputTests
{
    private const string ThrowingPath = "/razorkit-preview/preview/throwing/default";
    private const string TypedPath = "/razorkit-preview/preview/typed/default";
    private const string MarkupName = "<img src=x onerror=1>";

    // Malformed JSON whose parser message quotes the offending character from the request.
    private static readonly string MalformedProps = "{" + MarkupName + ": 1}";

    private static readonly string[] StackTraceMarkers = ["   at ", "StackTrace", "System.Text.Json.ThrowHelper"];

    private static async Task<string> PostAsync(TestHost host, string propsJson, string path = PreviewPath)
    {
        var token = await LoadShellAsync(host.Client);
        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, propsJson, path: path));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static void AssertNoStackTrace(string html)
    {
        foreach (var marker in StackTraceMarkers)
        {
            Assert.DoesNotContain(marker, html);
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Malformed_props_never_put_request_text_into_html_unencoded(string environment)
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions { EnvironmentName = environment });

        var html = await PostAsync(host, MalformedProps);

        Assert.DoesNotContain("<img", html);
        AssertNoStackTrace(html);
    }

    [Fact]
    public async Task Malformed_props_outside_development_show_only_the_generic_message()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions { EnvironmentName = Environments.Production });

        var html = await PostAsync(host, MalformedProps);

        Assert.Contains("The props are not valid JSON.", html);
        Assert.Contains("Check the application log", html);
        Assert.DoesNotContain("is invalid", html);
        Assert.DoesNotContain("&lt;", html);
    }

    [Fact]
    public async Task Malformed_props_in_development_show_the_detail_encoded()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions { EnvironmentName = Environments.Development });

        var html = await PostAsync(host, MalformedProps);

        Assert.Contains("&lt;", html);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Wrongly_typed_props_under_a_markup_name_do_not_reach_html_unencoded(string environment)
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions { EnvironmentName = environment });

        var html = await PostAsync(host, "{\"" + MarkupName + "\": 1.5, \"Label\": 1.5}");

        Assert.DoesNotContain("<img", html);
        AssertNoStackTrace(html);
        Assert.Contains("Click me", html);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task A_view_that_throws_renders_no_stack_trace_and_is_logged(string environment)
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await TestHost.StartAsync(new TestHostOptions { EnvironmentName = environment, LoggerProvider = logs });

        var html = await PostAsync(host, "{\"Label\":\"x\"}", ThrowingPath);

        AssertNoStackTrace(html);
        Assert.DoesNotContain("<b>marker", html);
        Assert.Contains(logs.Entries, e =>
            e.Category == "Manifesto.RazorKit.Controllers.RazorKitController"
            && e.Level == LogLevel.Error
            && e.Message.Contains("view failure"));
    }

    [Fact]
    public async Task A_view_that_throws_shows_its_message_encoded_in_development_only()
    {
        await using var dev = await TestHost.StartAsync(new TestHostOptions { EnvironmentName = Environments.Development });
        await using var prod = await TestHost.StartAsync(new TestHostOptions { EnvironmentName = Environments.Production });

        var devHtml = await PostAsync(dev, "{\"Label\":\"x\"}", ThrowingPath);
        var prodHtml = await PostAsync(prod, "{\"Label\":\"x\"}", ThrowingPath);

        Assert.Contains("view failure &lt;b&gt;marker&lt;/b&gt;", devHtml);
        Assert.DoesNotContain("view failure", prodHtml);
    }

    [Fact]
    public async Task Prop_values_are_not_written_to_the_log()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await TestHost.StartAsync(new TestHostOptions { LoggerProvider = logs });

        await PostAsync(host, "{\"Label\":\"secret-prop-value\", \"Count\": 1.5}", ThrowingPath);
        await PostAsync(host, "{\"secret-prop-value\": }");

        // The framework's own Trace logging records action arguments; that is the host's logging
        // configuration, so only RazorKit's categories are checked.
        var razorKitEntries = logs.Entries.Where(e => e.Category.StartsWith("Manifesto.RazorKit", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(razorKitEntries);
        Assert.DoesNotContain(razorKitEntries, e => e.Message.Contains("secret-prop-value") || e.State.Contains("secret-prop-value"));
    }

    [Theory]
    [InlineData("{\"Count\":\"secret-prop-value\"}")]
    [InlineData("{\"Enabled\":\"secret-prop-value\"}")]
    [InlineData("{\"Mode\":\"secret-prop-value\"}")]
    [InlineData("{\"secret-prop-value\": }")]
    [InlineData("{\"Label\":{\"secret-prop-value\":}}")]
    public async Task Values_that_fail_conversion_never_reach_the_log_or_exception_text(string propsJson)
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await TestHost.StartAsync(new TestHostOptions { LoggerProvider = logs });

        await PostAsync(host, propsJson, TypedPath);

        var razorKitEntries = logs.Entries.Where(e => e.Category.StartsWith("Manifesto.RazorKit", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(razorKitEntries, e => e.Message.Contains("secret-prop-value") || e.State.Contains("secret-prop-value"));
    }

    [Fact]
    public async Task A_value_that_fails_conversion_is_logged_by_property_and_exception_type_only()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await TestHost.StartAsync(new TestHostOptions { LoggerProvider = logs });

        var html = await PostAsync(host, "{\"Count\":\"secret-prop-value\"}", TypedPath);

        Assert.Contains("Typed default label", html);
        Assert.Contains(logs.Entries, e =>
            e.Category == "Manifesto.RazorKit.Services.ComponentPropertyService"
            && e.State.Contains("PropertyName=Count")
            && e.State.Contains("ExceptionType=FormatException"));
    }

    [Fact]
    public async Task An_unknown_component_gets_a_fixed_message()
    {
        await using var host = await TestHost.StartAsync();
        var token = await LoadShellAsync(host.Client);

        var response = await host.Client.SendAsync(RenderPost(token.CookieValue, token.PageValue, path: "/razorkit-preview/preview/%3Cimg%20src=x%3E/default"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<img", html);
    }
}
