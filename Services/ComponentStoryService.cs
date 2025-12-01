using System.Reflection;
using Manifesto.RazorKit.Models;

namespace Manifesto.RazorKit.Services;

/// <summary>
/// Service for discovering and retrieving component stories
/// </summary>
public class ComponentStoryService
{
    private readonly Dictionary<string, IComponentStories> _discoveredStories;

    public ComponentStoryService()
    {
        _discoveredStories = DiscoverStoryFiles();
    }

    /// <summary>
    /// Gets all stories for a specific component
    /// </summary>
    public List<ComponentStory> GetStoriesForComponent(string componentName)
    {
        // Try to use discovered story files from any loaded assembly
        if (_discoveredStories.TryGetValue(componentName.ToLower(), out IComponentStories? storyClass))
        {
            try
            {
                return storyClass.GetStories();
            }
            catch (Exception)
            {
                // Return empty list if there's an error loading stories
                return new List<ComponentStory>();
            }
        }

        // Return empty list if no stories found
        return new List<ComponentStory>();
    }

    private Dictionary<string, IComponentStories> DiscoverStoryFiles()
    {
        var stories = new Dictionary<string, IComponentStories>();

        try
        {
            // Scan all loaded assemblies for component stories
            IEnumerable<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));

            foreach (Assembly assembly in assemblies)
            {
                try
                {
                    // Find all types that implement IComponentStories
                    var storyTypes = assembly.GetTypes()
                        .Where(t => typeof(IComponentStories).IsAssignableFrom(t) &&
                                   !t.IsInterface &&
                                   !t.IsAbstract)
                        .ToList();

                    foreach (Type storyType in storyTypes)
                    {
                        try
                        {
                            // Create instance of the story class
                            if (Activator.CreateInstance(storyType) is IComponentStories instance)
                            {
                                stories[instance.ComponentName.ToLower()] = instance;
                            }
                        }
                        catch (Exception)
                        {
                            // Skip this story class if it can't be instantiated
                            continue;
                        }
                    }
                }
                catch (Exception)
                {
                    // Skip assemblies that can't be scanned
                    continue;
                }
            }
        }
        catch (Exception)
        {
            // If discovery fails, return empty dictionary
        }

        return stories;
    }
}
