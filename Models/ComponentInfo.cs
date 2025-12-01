namespace Manifesto.RazorKit.Models;

/// <summary>
/// Information about a discovered component
/// </summary>
public class ComponentInfo
{
    public string Name { get; set; } = string.Empty;
    public string AtomicLevel { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public Type? ModelType { get; set; }
    public int Order { get; set; }
}
