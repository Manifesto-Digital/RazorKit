using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace Manifesto.RazorKit.Filters;

internal static class RazorKitRequestKind
{
    /// <summary>
    /// A render is a request with a non-safe method, or one that reached an action declaring a
    /// non-safe method. <c>Request.Method</c> alone is not trusted: middleware such as
    /// <c>UseHttpMethodOverride</c> can turn a POST into a GET after routing has selected the POST
    /// action, which would otherwise charge a render to the page bucket and skip the concurrency
    /// backstop. The GET preview declares only GET, so it stays a page load.
    /// </summary>
    public static bool IsRender(HttpRequest request, ActionDescriptor descriptor) =>
        (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        || DeclaredMethods.DeclaresUnsafeMethod(descriptor);
}
