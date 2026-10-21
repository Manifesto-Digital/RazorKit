using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// Answers questions about an action from the HTTP methods it declares, which are fixed when the
/// action is discovered. HttpRequest.Method is not: host middleware such as method override can
/// rewrite it after routing has chosen the action, so it cannot decide whether a filter applies.
/// </summary>
internal static class DeclaredMethods
{
    /// <summary>
    /// True when the action may accept a method other than GET or HEAD: its HTTP method constraint
    /// names one, or it has no constraint at all. An unconstrained controller action accepts every
    /// method, so it counts as unsafe; otherwise a future action added without a method attribute
    /// would lose protection whenever the method is rewritten to GET.
    /// The one exception is a Razor Page, which never has a method constraint: it returns false and
    /// the caller falls back to the request method. That is safe because RazorKit's page answers
    /// only GET and HEAD (the method check) and its GET handler binds nothing from the body.
    /// </summary>
    public static bool DeclaresUnsafeMethod(ActionDescriptor descriptor)
    {
        if (descriptor is PageActionDescriptor)
        {
            return false;
        }

        var declared = descriptor.ActionConstraints?
            .OfType<HttpMethodActionConstraint>()
            .SelectMany(c => c.HttpMethods)
            .ToList();

        if (declared is null || declared.Count == 0)
        {
            return true;
        }

        return declared.Any(method => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method));
    }
}
