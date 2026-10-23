using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Registry;

namespace Manifesto.RazorKit.Services;

/// <summary>
/// Service for discovering and retrieving component stories
/// </summary>
public class ComponentStoryService
{
    private readonly RazorKitRegistry _registry;

    public ComponentStoryService(RazorKitRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Gets all stories for a specific component
    /// </summary>
    public List<ComponentStory> GetStoriesForComponent(string componentName)
    {
        Type? storiesType = _registry.FindStoriesType(componentName);
        if (storiesType == null)
        {
            return new List<ComponentStory>();
        }

        try
        {
            // A new instance every call. Stories pass live references into the props a view
            // receives, so reusing an instance would let one request's mutations reach the next.
            return ((IComponentStories)Activator.CreateInstance(storiesType)!).GetStories();
        }
        catch (Exception)
        {
            // Return empty list if there's an error loading stories
            return new List<ComponentStory>();
        }
    }
}
