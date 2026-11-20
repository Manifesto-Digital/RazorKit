using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// Marks every RazorKit response as uncacheable and unindexable, and appends a restrictive
/// Content-Security-Policy. Does no I/O. An endpoint whose output is a pure function of its URL
/// can set <see cref="SuccessCacheControl"/>, which replaces no-store on a 200 only.
/// </summary>
internal sealed class ResponseHeadersFilter : IResourceFilter
{
    internal const string ContentSecurityPolicy =
        "object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'self'";

    /// <summary>
    /// Set in <c>HttpContext.Items</c> by an action that produced a cacheable body itself.
    /// </summary>
    internal const string CacheableItemKey = "Manifesto.RazorKit.CacheableResponse";

    /// <summary>
    /// Cache-Control for a 200 response that the action marked with <see cref="CacheableItemKey"/>.
    /// The marker, not the status alone, decides: a host's status-code page re-executed as a 200
    /// would otherwise inherit a long cache lifetime (web cache deception), and a cached 429 or
    /// 404 would keep failing after the cause had gone.
    /// </summary>
    public string? SuccessCacheControl { get; init; }

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var response = context.HttpContext.Response;

        // OnStarting rather than setting headers now: it runs when the response begins, so the
        // headers survive Response.Clear() and a host's UseExceptionHandler re-execution.
        response.OnStarting(static state =>
        {
            var (httpResponse, successCacheControl) = ((HttpResponse, string?))state;
            var headers = httpResponse.Headers;
            headers.CacheControl = successCacheControl is not null
                && httpResponse.StatusCode == StatusCodes.Status200OK
                && httpResponse.HttpContext.Items.ContainsKey(CacheableItemKey)
                ? successCacheControl
                : "no-store";
            headers["X-Robots-Tag"] = "noindex";

            // Appended, never assigned: a policy the host already set stays enforced, so
            // RazorKit can only tighten what the host asked for.
            headers.Append("Content-Security-Policy", ContentSecurityPolicy);
            return Task.CompletedTask;
        }, (response, SuccessCacheControl));
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
