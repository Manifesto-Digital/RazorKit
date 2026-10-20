using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// Marks every RazorKit response as uncacheable and unindexable, and appends a restrictive
/// Content-Security-Policy. Does no I/O.
/// </summary>
internal sealed class ResponseHeadersFilter : IResourceFilter
{
    internal const string ContentSecurityPolicy =
        "object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'self'";

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var response = context.HttpContext.Response;

        // OnStarting rather than setting headers now: it runs when the response begins, so the
        // headers survive Response.Clear() and a host's UseExceptionHandler re-execution.
        response.OnStarting(static state =>
        {
            var headers = ((HttpResponse)state).Headers;
            headers.CacheControl = "no-store";
            headers["X-Robots-Tag"] = "noindex";

            // Appended, never assigned: a policy the host already set stays enforced, so
            // RazorKit can only tighten what the host asked for.
            headers.Append("Content-Security-Policy", ContentSecurityPolicy);
            return Task.CompletedTask;
        }, response);
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
