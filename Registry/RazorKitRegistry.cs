using System.Collections.Concurrent;
using System.Reflection;
using Manifesto.RazorKit.Models;
using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.Logging;

namespace Manifesto.RazorKit.Registry;

/// <summary>
/// Everything RazorKit learns about the host's components by scanning loaded assemblies, built
/// once per process on first use and shared by every request. Holds types and strings only:
/// story instances are created per request, because a story hands live references into the
/// props a view receives, and a view that mutates them must not affect the next request.
/// </summary>
public sealed class RazorKitRegistry
{
    private static readonly Dictionary<string, Type> KnownConcreteTypes = new()
    {
        { typeof(IHtmlContent).FullName!, typeof(HtmlString) }
    };

    /// <summary>
    /// Serves interface lookups for code that has no access to the container. It never scans for
    /// components (nothing asks it to), so it costs nothing until an interface is looked up.
    /// </summary>
    internal static RazorKitRegistry ForInterfaceLookups { get; } =
        new(() => new RegistrySnapshot());

    private readonly Lazy<RegistrySnapshot> _snapshot;
    private readonly ConcurrentDictionary<Type, Type?> _concreteTypes = new();

    internal RazorKitRegistry(ILogger<RazorKitRegistry> logger)
        : this(() => RegistryScanner.Scan(logger))
    {
    }

    // The factory is a seam so tests can observe how often the scan runs.
    internal RazorKitRegistry(Func<RegistrySnapshot> scan)
    {
        // ExecutionAndPublication: concurrent first requests wait for one scan instead of each running their own.
        _snapshot = new Lazy<RegistrySnapshot>(scan, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Components in explorer order. Not a copy: callers must not modify it.</summary>
    internal IReadOnlyList<RegisteredComponent> Components => _snapshot.Value.Components;

    internal Type? FindPropsType(string componentName) =>
        _snapshot.Value.PropsTypes.GetValueOrDefault(componentName);

    internal Type? FindStoriesType(string componentName) =>
        _snapshot.Value.StoriesTypes.GetValueOrDefault(componentName);

    /// <summary>
    /// The concrete type behind an interface used as a props member, by the convention
    /// <c>Namespace.IName</c> -> <c>Namespace.Name</c>. Resolved on first request for each interface and remembered.
    /// </summary>
    internal Type? FindConcreteType(Type interfaceType)
    {
        if (!interfaceType.IsInterface)
        {
            return null;
        }

        return _concreteTypes.GetOrAdd(interfaceType, ResolveConcreteType);
    }

    private static Type? ResolveConcreteType(Type interfaceType)
    {
        if (KnownConcreteTypes.TryGetValue(interfaceType.FullName ?? string.Empty, out var known))
        {
            return known;
        }

        var interfaceName = interfaceType.Name;
        if (!interfaceName.StartsWith('I') || interfaceName.Length <= 1)
        {
            return null;
        }

        var concreteTypeName = $"{interfaceType.Namespace}.{interfaceName[1..]}";

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.FullName)))
        {
            try
            {
                var candidate = assembly.GetType(concreteTypeName, throwOnError: false);
                if (candidate != null && interfaceType.IsAssignableFrom(candidate) && !candidate.IsAbstract)
                {
                    return candidate;
                }
            }
            catch
            {
                // An assembly that cannot be inspected is skipped, as the scans always did.
            }
        }

        return null;
    }
}

/// <summary>A discovered component. Immutable, because one instance is shared by all requests.</summary>
internal sealed record RegisteredComponent(
    string Name,
    string AtomicLevel,
    string Path,
    string ResourceName,
    Type? ModelType,
    int Order);

internal sealed class RegistrySnapshot
{
    public IReadOnlyList<RegisteredComponent> Components { get; init; } = Array.Empty<RegisteredComponent>();

    /// <summary>Keyed by component name, case-insensitive. Value is the <c>{Name}Props</c> type.</summary>
    public IReadOnlyDictionary<string, Type> PropsTypes { get; init; } =
        new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keyed by <see cref="IComponentStories.ComponentName"/>, case-insensitive.</summary>
    public IReadOnlyDictionary<string, Type> StoriesTypes { get; init; } =
        new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
}

internal static class RegistryScanner
{
    private const string PropsSuffix = "Props";
    private const string StoriesSuffix = "Stories";

    public static RegistrySnapshot Scan(ILogger logger)
    {
        var components = new List<RegisteredComponent>();
        var propsTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        var storiesTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)))
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (Exception)
            {
                // An assembly that cannot be scanned is skipped.
                continue;
            }

            var assemblyProps = IndexPropsTypes(types, logger);
            foreach (var (name, type) in assemblyProps)
            {
                if (!propsTypes.TryAdd(name, type))
                {
                    LogDuplicate(logger, "props type", name, type);
                }
            }

            components.AddRange(types
                .Where(IsComponentStoriesName)
                .Select(storyType => ToComponent(storyType, assemblyProps)));

            foreach (var storyType in types.Where(IsStoriesImplementation))
            {
                var componentName = ReadComponentName(storyType);
                if (componentName is null)
                {
                    continue;
                }

                if (!storiesTypes.TryAdd(componentName, storyType))
                {
                    LogDuplicate(logger, "stories type", componentName, storyType);
                }
            }
        }

        var ordered = components
            .GroupBy(c => $"{c.AtomicLevel}.{c.Name}")
            .Select(g => g.First())
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Name)
            .ToList();

        foreach (var duplicate in ordered.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            LogDuplicate(logger, "component", duplicate.Key, null);
        }

        return new RegistrySnapshot
        {
            Components = ordered,
            PropsTypes = propsTypes,
            StoriesTypes = storiesTypes
        };
    }

    // Keyed the way the old per-request scan matched: a type named "{component}Props", ignoring case.
    private static Dictionary<string, Type> IndexPropsTypes(Type[] types, ILogger logger)
    {
        var index = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (type.Name.EndsWith(PropsSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var name = type.Name[..^PropsSuffix.Length];
                if (!index.TryAdd(name, type))
                {
                    LogDuplicate(logger, "props type", name, type);
                }
            }
        }

        return index;
    }

    private static bool IsComponentStoriesName(Type type) =>
        type.Name.EndsWith(StoriesSuffix) && type.Namespace != null && type.Namespace.Contains("Components");

    private static bool IsStoriesImplementation(Type type) =>
        typeof(IComponentStories).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract;

    private static RegisteredComponent ToComponent(Type storyType, Dictionary<string, Type> assemblyProps)
    {
        var componentName = storyType.Name.Replace(StoriesSuffix, "");

        // Components.Atoms.Button -> Atoms
        var namespaceParts = storyType.Namespace?.Split('.') ?? Array.Empty<string>();
        var level = "Unknown";
        for (var i = 0; i < namespaceParts.Length - 1; i++)
        {
            if (namespaceParts[i] == "Components")
            {
                level = namespaceParts[i + 1];
                break;
            }
        }

        return new RegisteredComponent(
            Name: componentName,
            AtomicLevel: level,
            Path: $"~/Components/{level}/{componentName}/{componentName}.cshtml",
            ResourceName: storyType.FullName ?? string.Empty,
            ModelType: assemblyProps.GetValueOrDefault(componentName),
            Order: GetAtomicOrder(level));
    }

    // A throwaway instance, only to read the name; it is never kept or reused.
    private static string? ReadComponentName(Type storyType)
    {
        try
        {
            return (Activator.CreateInstance(storyType) as IComponentStories)?.ComponentName;
        }
        catch (Exception)
        {
            // A stories class that cannot be instantiated is skipped.
            return null;
        }
    }

    private static int GetAtomicOrder(string level) =>
        level.ToLowerInvariant() switch
        {
            "atoms" => 1,
            "molecules" => 2,
            "organisms" => 3,
            "templates" => 4,
            "pages" => 5,
            _ => 99
        };

    private static void LogDuplicate(ILogger logger, string kind, string name, Type? type) =>
        logger.LogWarning(
            "RazorKit found more than one {Kind} named {Name}{Type}; the first one found is used.",
            kind, name, type is null ? string.Empty : $" ({type.FullName})");
}
