using System.Reflection;

namespace Manifesto.RazorKit.Models;

/// <summary>
/// Interface for component story definitions
/// </summary>
public interface IComponentStories
{
    /// <summary>
    /// The name of the component these stories belong to
    /// </summary>
    public string ComponentName { get; }

    /// <summary>
    /// Get all stories for this component
    /// </summary>
    /// <returns>List of component stories</returns>
    public List<ComponentStory> GetStories();
}

/// <summary>
/// Base class for component story definitions
/// </summary>
/// <typeparam name="TProps">The props type for the component</typeparam>
public abstract class ComponentStoriesBase<TProps> : IComponentStories where TProps : class, new()
{
    public abstract string ComponentName { get; }

    public abstract List<ComponentStory> GetStories();

    /// <summary>
    /// Helper method to create a story with typed props
    /// </summary>
    protected ComponentStory CreateStory(string name, string displayName, string description, TProps props)
    {
        var properties = new Dictionary<string, object>();

        // Use reflection to convert props to dictionary
        Type propsType = typeof(TProps);
        foreach (PropertyInfo property in propsType.GetProperties())
        {
            if (property.CanRead)
            {
                var value = property.GetValue(props);
                if (value != null)
                {
                    properties[property.Name] = value;
                }
            }
        }

        return new ComponentStory
        {
            Name = name,
            DisplayName = displayName,
            Description = description,
            Properties = properties
        };
    }
}

/// <summary>
/// Represents a specific story/variant of a component
/// </summary>
public class ComponentStory
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
}
