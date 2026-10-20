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
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<ResponseHeadersFilter>();
}
