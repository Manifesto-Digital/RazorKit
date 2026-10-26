using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Manifesto.RazorKit.Filters;

internal static class RazorKitRequestKind
{
    /// <summary>
    /// A render is a request with a non-safe method, or one that reached an action declaring a
    /// non-safe method. <c>Request.Method</c> alone is not trusted: middleware such as
    /// <c>UseHttpMethodOverride</c> can turn a POST into a GET after routing has selected the POST
    /// action, which would otherwise charge a render to the page bucket and skip the concurrency
    /// backstop. The GET preview declares only GET, so it stays a page load for the rate limit; see
    /// <see cref="TakesRenderSlot"/> for the concurrency cap, which it does count against.
    /// </summary>
    public static bool IsRender(HttpRequest request, ActionDescriptor descriptor) =>
        (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        || DeclaredMethods.DeclaresUnsafeMethod(descriptor);

    /// <summary>
    /// Whether the request holds a render slot: every request that runs a component, which is every
    /// action on the controller, the GET preview included, plus anything <see cref="IsRender"/>
    /// treats as a render. This is wider than the rate-limit bucket choice on purpose: a GET preview
    /// renders a component like the POST does, so it counts against the cap on concurrent work, but
    /// it is still a page load for the per-client bucket. The decision comes from the action
    /// descriptor, never from <c>Request.Method</c>. The page shell never renders a component and
    /// takes no slot.
    /// </summary>
    public static bool TakesRenderSlot(HttpRequest request, ActionDescriptor descriptor) =>
        descriptor is not PageActionDescriptor || IsRender(request, descriptor);
}
