using System.Reflection;
using Manifesto.RazorKit.Models;
using Microsoft.AspNetCore.Hosting;

namespace Manifesto.RazorKit.Services;

/// <summary>
/// Service for discovering components across loaded assemblies
/// </summary>
public class ComponentDiscoveryService
{
    private readonly IWebHostEnvironment _environment;

    public ComponentDiscoveryService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    /// <summary>
    /// Discovers all components by scanning for Story types
    /// </summary>
    public List<ComponentInfo> DiscoverComponents()
    {
        List<ComponentInfo> components = DiscoverFromStoriesTypes();

        return components
            .GroupBy(c => $"{c.AtomicLevel}.{c.Name}")
            .Select(g => g.First())
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Name)
            .ToList();
    }

    private List<ComponentInfo> DiscoverFromStoriesTypes()
    {
        var components = new List<ComponentInfo>();

        // Scan all loaded assemblies
        IEnumerable<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));

        foreach (Assembly assembly in assemblies)
        {
            try
            {
                // Find all types that end with "Stories" in the Components namespace
                var storyTypes = assembly.GetTypes()
                    .Where(t => t.Name.EndsWith("Stories") &&
                               t.Namespace != null &&
                               t.Namespace.Contains("Components"))
                    .ToList();

                foreach (Type storyType in storyTypes)
                {
                    // Simple extraction: ButtonStories -> Button
                    var componentName = storyType.Name.Replace("Stories", "");

                    // Extract level from namespace: Components.Atoms.Button -> Atoms
                    var namespaceParts = storyType.Namespace?.Split('.') ?? Array.Empty<string>();
                    var level = "Unknown";

                    for (int i = 0; i < namespaceParts.Length - 1; i++)
                    {
                        if (namespaceParts[i] == "Components" && i + 1 < namespaceParts.Length)
                        {
                            level = namespaceParts[i + 1];
                            break;
                        }
                    }

                    var componentInfo = new ComponentInfo
                    {
                        Name = componentName,
                        AtomicLevel = level,
                        Path = $"~/Components/{level}/{componentName}/{componentName}.cshtml",
                        ResourceName = storyType.FullName ?? string.Empty,
                        ModelType = GetModelType(assembly, componentName),
                        Order = GetAtomicOrder(level)
                    };

                    components.Add(componentInfo);
                }
            }
            catch (Exception)
            {
                // Skip assemblies that can't be scanned
                continue;
            }
        }

        return components;
    }

    private Type? GetModelType(Assembly assembly, string componentName)
    {
        try
        {
            // Try to find the Props type in the same assembly
            return assembly.GetTypes()
                .FirstOrDefault(t => t.Name.Equals($"{componentName}Props", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private int GetAtomicOrder(string level)
    {
        return level.ToLower() switch
        {
            "atoms" => 1,
            "molecules" => 2,
            "organisms" => 3,
            "templates" => 4,
            "pages" => 5,
            _ => 99
        };
    }
}
