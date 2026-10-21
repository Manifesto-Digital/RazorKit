using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// Applies <see cref="MethodCheckFilter"/> to RazorKit's page model. Needed because a Razor Page
/// with no handler for the request's method still executes and renders.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
internal sealed class MethodCheckAttribute : Attribute, IFilterFactory, IOrderedFilter
{
    public int Order { get; set; } = RazorKitFilterOrder.MethodCheck;

    public bool IsReusable => true;

    // GetRequiredService so a host that skipped AddRazorKit fails closed with a 500.
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<MethodCheckFilter>();
}
