using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// Turns away clients that have used up their bucket with 429 and a Retry-After. The client is
/// identified by <c>Connection.RemoteIpAddress</c> alone.
/// </summary>
internal sealed class RateLimitFilter : IResourceFilter
{
    private readonly RazorKitRateLimiter _limiter;

    public RateLimitFilter(RazorKitRateLimiter limiter)
    {
        _limiter = limiter;
    }

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var http = context.HttpContext;
        var bucket = RazorKitRequestKind.IsRender(http.Request, context.ActionDescriptor) ? RateLimitBucket.Render : RateLimitBucket.Page;

        var decision = _limiter.TryTakeToken(bucket, http.Connection.RemoteIpAddress);
        if (decision.Allowed)
        {
            return;
        }

        http.Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Result = RejectionPage.Result(StatusCodes.Status429TooManyRequests, RejectionPage.TooManyRequests);
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
