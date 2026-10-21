using Manifesto.RazorKit.Filters;
using Manifesto.RazorKit.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Manifesto.RazorKit.Tests;

/// <summary>Filter behaviour that cannot be observed through a test server.</summary>
public class RequestProtectionFilterTests
{
    private sealed class UnreadableStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The body was read.");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The body was read.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The body was read.");
    }

    [Fact]
    public async Task A_declared_length_over_the_cap_is_rejected_without_reading_the_body()
    {
        var options = new RazorKitOptions { MaxPropsJsonBytes = 1024 };
        var filter = new RequestProtectionFilter(options, AllowedOriginList.Create(Array.Empty<string>()), NullLogger<RequestProtectionFilter>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.ContentLength = 1024 * 1024;
        httpContext.Request.Body = new UnreadableStream();
        var context = new ResourceExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new List<IValueProviderFactory>());

        await filter.OnResourceExecutionAsync(context, () => throw new InvalidOperationException("The pipeline continued."));

        var result = Assert.IsType<ContentResult>(context.Result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
    }
}
