using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Manifesto.RazorKit.Filters;

/// <summary>Answers anything other than GET and HEAD with a 405 and a fixed page.</summary>
internal sealed class MethodCheckFilter : IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        // Reads the request method, which host method-override middleware can change after routing.
        // That is acceptable here, unlike in request protection: a page request rewritten to GET or
        // HEAD runs OnGet, which binds nothing from the request body.
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
        {
            return;
        }

        context.HttpContext.Response.Headers.Allow = "GET, HEAD";
        context.Result = RejectionPage.Result(StatusCodes.Status405MethodNotAllowed, RejectionPage.MethodNotAllowed);
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
