using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// Applies <see cref="ResponseHeadersFilter"/> to RazorKit's controller and page model.
/// A filter attribute on the type, rather than an entry in MvcOptions.Filters, travels with
/// RazorKit's endpoints however the host maps them, and never touches the host's own actions.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
internal sealed class ResponseHeadersAttribute : Attribute, IFilterFactory, IOrderedFilter
{
    public int Order { get; set; } = RazorKitFilterOrder.ResponseHeaders;

    public bool IsReusable => true;

    // GetRequiredService, not ActivatorUtilities: a host that mapped RazorKit but skipped
    // AddRazorKit gets an exception (a 500) instead of a response without these headers.
    /// <summary>
    /// For endpoints whose own 200 response never changes for a given URL: lets the action opt in
    /// (see <see cref="ResponseHeadersFilter.CacheableItemKey"/>) to a year-long private cache
    /// instead of no-store. Private, so a shared cache that ignores the query string cannot be
    /// poisoned for every user.
    /// </summary>
    public bool ImmutableOnSuccess { get; init; }

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var shared = serviceProvider.GetRequiredService<ResponseHeadersFilter>();
        return ImmutableOnSuccess
            ? new ResponseHeadersFilter { SuccessCacheControl = "private, max-age=31536000, immutable" }
            : shared;
    }
}
