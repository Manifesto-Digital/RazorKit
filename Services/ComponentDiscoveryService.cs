using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Registry;

namespace Manifesto.RazorKit.Services;

/// <summary>
/// Service for discovering components across loaded assemblies
/// </summary>
public class ComponentDiscoveryService
{
    private readonly RazorKitRegistry _registry;

    public ComponentDiscoveryService(RazorKitRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Returns every discovered component in explorer order. The scan happens once per process;
    /// each call returns new <see cref="ComponentInfo"/> objects, so callers may modify them freely.
    /// </summary>
    public List<ComponentInfo> DiscoverComponents() =>
        _registry.Components
            .Select(c => new ComponentInfo
            {
                Name = c.Name,
                AtomicLevel = c.AtomicLevel,
                Path = c.Path,
                ResourceName = c.ResourceName,
                ModelType = c.ModelType,
                Order = c.Order
            })
            .ToList();
}
