using Manifesto.RazorKit.Filters;

namespace Manifesto.RazorKit.Tests;

public class FailClosedTests
{
    [Theory]
    [InlineData("/razorkit/button/default")]
    [InlineData("/razorkit-preview/preview/button/default")]
    public async Task Host_that_skips_AddRazorKit_never_serves_a_successful_response(string path)
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions { AddRazorKit = false });

        HttpResponseMessage? response = null;
        var exception = await Record.ExceptionAsync(async () => response = await host.Client.GetAsync(path));

        // Without a handler the test server surfaces the failure as an exception; with one it is a 500.
        if (exception is null)
        {
            Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response!.StatusCode);
        }
    }

    [Fact]
    public void Headers_attribute_throws_when_its_filter_is_not_registered()
    {
        using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => new ResponseHeadersAttribute().CreateInstance(services));
    }
}
