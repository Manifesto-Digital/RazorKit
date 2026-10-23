using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// Applies <see cref="RequestProtectionFilter"/> at class level, so an action added later is
/// protected without anyone remembering an attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
internal sealed class RequestProtectionAttribute : Attribute, IFilterFactory, IOrderedFilter
{
    public int Order { get; set; } = RazorKitFilterOrder.RequestProtection;

    public bool IsReusable => true;

    // GetRequiredService so a host that skipped AddRazorKit fails closed with a 500 rather than
    // running RazorKit's actions unprotected.
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<RequestProtectionFilter>();
}
