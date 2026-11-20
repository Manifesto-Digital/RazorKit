using Manifesto.RazorKit.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
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
        IsRender(request, descriptor) || (descriptor is not PageActionDescriptor && !IsPlaceholderImage(descriptor));

    // The placeholder controller carries the concurrency filter like every other controller, so
    // an action added to it later that renders still takes a slot (IsRender catches a non-safe
    // method). Only its GET image, which runs no component and is cheap, is exempt.
    private static bool IsPlaceholderImage(ActionDescriptor descriptor) =>
        descriptor is ControllerActionDescriptor { ControllerTypeInfo: { } type }
        && type.AsType() == typeof(RazorKitPlaceholderController);
}
