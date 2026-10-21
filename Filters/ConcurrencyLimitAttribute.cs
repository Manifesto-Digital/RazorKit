using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// Applies <see cref="ConcurrencyLimitFilter"/> to RazorKit's rendering controller. Not on the
/// page model, which never renders a component.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
internal sealed class ConcurrencyLimitAttribute : Attribute, IFilterFactory, IOrderedFilter
{
    public int Order { get; set; } = RazorKitFilterOrder.ConcurrencyLimit;

    public bool IsReusable => true;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<ConcurrencyLimitFilter>();
}
