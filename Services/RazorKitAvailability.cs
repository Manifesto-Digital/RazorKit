using Manifesto.RazorKit.Controllers;
using Manifesto.RazorKit.Models;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;

namespace Manifesto.RazorKit.Services;

/// <summary>
/// Whether RazorKit is served in the current environment. The environment is fixed for the life
/// of the process, so this is decided once.
/// </summary>
internal sealed class RazorKitAvailability
{
    private RazorKitAvailability(bool isEnabled, string environmentName)
    {
        IsEnabled = isEnabled;
        EnvironmentName = environmentName;
    }

    public bool IsEnabled { get; }

    public string EnvironmentName { get; }

    public static RazorKitAvailability For(RazorKitOptions options, IHostEnvironment environment) =>
        new(options.EnabledEnvironments is null
                || options.EnabledEnvironments.Contains(environment.EnvironmentName, StringComparer.OrdinalIgnoreCase),
            environment.EnvironmentName);

    /// <summary>Throws for a null or blank entry, which can only be a mistake and never a name that matches.</summary>
    public static void Validate(IList<string>? enabledEnvironments)
    {
        if (enabledEnvironments is not null && enabledEnvironments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "RazorKit EnabledEnvironments entries must be environment names, such as \"Development\". Remove the blank entry.",
                nameof(enabledEnvironments));
        }
    }
}

/// <summary>
/// Removes RazorKit's controllers and page from MVC's application model when RazorKit is disabled,
/// so no RazorKit endpoint exists and the host's normal 404 handling answers, indistinguishable
/// from any unknown URL. Works on the model rather than on named routes, so any action RazorKit
/// adds later is covered without further changes.
/// </summary>
/// <remarks>
/// A filter was rejected: it would run after routing, so the response would need to be built to
/// look like the host's 404, and RazorKit's own endpoint metadata (and, for the page, its
/// handler selection and 405 behaviour) would still be observable.
/// </remarks>
internal sealed class RazorKitAvailabilityConvention : IApplicationModelConvention, IPageRouteModelConvention
{
    private static readonly System.Reflection.Assembly RazorKitAssembly = typeof(RazorKitController).Assembly;

    private const string PageRelativePath = "/Pages/RazorKit.cshtml";

    private readonly RazorKitAvailability _availability;

    public RazorKitAvailabilityConvention(RazorKitAvailability availability)
    {
        _availability = availability;
    }

    public void Apply(ApplicationModel application)
    {
        if (_availability.IsEnabled)
        {
            return;
        }

        foreach (var controller in application.Controllers.Where(c => c.ControllerType.Assembly == RazorKitAssembly).ToList())
        {
            application.Controllers.Remove(controller);
        }
    }

    public void Apply(PageRouteModel model)
    {
        if (!_availability.IsEnabled && string.Equals(model.RelativePath, PageRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            model.Selectors.Clear();
        }
    }
}
