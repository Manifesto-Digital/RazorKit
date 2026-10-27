using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// Caps renders in flight across all clients, answering 503 when none is free. Per-client buckets
/// alone cannot bound total render work, for example when many clients share one proxy address.
/// </summary>
internal sealed class ConcurrencyLimitFilter : IAsyncResourceFilter
{
    private readonly RazorKitRateLimiter _limiter;

    public ConcurrencyLimitFilter(RazorKitRateLimiter limiter)
    {
        _limiter = limiter;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (!RazorKitRequestKind.IsRender(context.HttpContext.Request, context.ActionDescriptor))
        {
            await next();
            return;
        }

        using var slot = new RenderSlot(_limiter.TryAcquireRenderSlot());
        if (!slot.IsAcquired)
        {
            context.HttpContext.Response.Headers.RetryAfter = "1";
            context.Result = RejectionPage.Result(StatusCodes.Status503ServiceUnavailable, RejectionPage.Busy);
            return;
        }

        // Given back when the response starts: the view has rendered by then, and what remains is
        // writing the body, which a slow reader could stretch out to hold every slot. The using
        // above covers requests that fail before a response starts.
        context.HttpContext.Response.OnStarting(static state =>
        {
            ((RenderSlot)state).Dispose();
            return Task.CompletedTask;
        }, slot);

        await next();
    }
}
