namespace Manifesto.RazorKit.Models;

/// <summary>
/// Metadata about a component property
/// </summary>
public class ComponentPropertyInfo
{
    public string Name { get; set; } = string.Empty;
    public Type Type { get; set; } = typeof(object);
    public object? DefaultValue { get; set; }
    public bool IsEnum { get; set; }
    public bool IsNullable { get; set; }
    public bool IsCollection { get; set; }
    public bool IsComplexType { get; set; }
    public string[]? EnumValues { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
}
