using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Manifesto.RazorKit.Converters;
using Manifesto.RazorKit.Helpers;
using Manifesto.RazorKit.Models;

namespace Manifesto.RazorKit.Services;

/// <summary>
/// Service for inspecting and manipulating component properties
/// </summary>
public class ComponentPropertyService
{
    /// <summary>
    /// Get metadata for all properties of a component props type
    /// </summary>
    public List<ComponentPropertyInfo> GetComponentProperties(Type? propsType)
    {
        if (propsType == null)
        {
            return new List<ComponentPropertyInfo>();
        }

        var properties = new List<ComponentPropertyInfo>();

        foreach (PropertyInfo prop in propsType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Skip inherited properties from interfaces unless they're defined on the concrete type
            if (prop.DeclaringType != propsType && prop.DeclaringType?.IsInterface == true)
            {
                continue;
            }

            var propInfo = new ComponentPropertyInfo
            {
                Name = prop.Name,
                Type = prop.PropertyType,
                DefaultValue = GetDefaultValue(prop.PropertyType),
                IsEnum = prop.PropertyType.IsEnum,
                IsNullable = Nullable.GetUnderlyingType(prop.PropertyType) != null || !prop.PropertyType.IsValueType,
                IsCollection = IsCollectionType(prop.PropertyType),
                IsComplexType = IsComplexType(prop.PropertyType),
                EnumValues = prop.PropertyType.IsEnum ? Enum.GetNames(prop.PropertyType) : null,
                DisplayName = GetDisplayName(prop),
                Description = GetDescription(prop)
            };

            properties.Add(propInfo);
        }

        return properties.OrderBy(p => p.Name).ToList();
    }

    private bool IsCollectionType(Type type)
    {
        // Check if it's a generic collection (List<T>, IList<T>, IEnumerable<T>, etc.)
        if (type.IsGenericType)
        {
            Type genericDef = type.GetGenericTypeDefinition();
            return genericDef == typeof(List<>) ||
                   genericDef == typeof(IList<>) ||
                   genericDef == typeof(ICollection<>) ||
                   genericDef == typeof(IEnumerable<>);
        }

        // Check if it implements IEnumerable (but not string)
        return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
    }

    private bool IsComplexType(Type type)
    {
        // Simple types that can be sent via query string
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type.IsEnum)
        {
            return false;
        }

        // Nullable simple types
        Type? underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
        {
            return IsComplexType(underlyingType);
        }

        // Collections are complex
        if (IsCollectionType(type))
        {
            return true;
        }

        // Everything else (custom classes, interfaces) is complex
        return true;
    }

    /// <summary>
    /// Creates an instance of the component props type with the given values
    /// </summary>
    public object CreateComponentInstance(Type propsType, Dictionary<string, object> values)
    {
        var instance = Activator.CreateInstance(propsType);

        if (instance == null)
        {
            return instance!;
        }

        foreach (KeyValuePair<string, object> kvp in values)
        {
            PropertyInfo? property = propsType.GetProperty(kvp.Key);
            if (property != null && property.CanWrite)
            {
                try
                {
                    var convertedValue = ConvertValue(kvp.Value, property.PropertyType);
                    property.SetValue(instance, convertedValue);
                }
                catch (Exception ex)
                {
                    // Log conversion error but continue
                    System.Diagnostics.Debug.WriteLine($"Error setting property {kvp.Key}: {ex.Message}");
                }
            }
        }

        return instance;
    }

    /// <summary>
    /// Gets default values for all properties of a component props type
    /// </summary>
    public Dictionary<string, object> GetDefaultValues(Type propsType)
    {
        var defaults = new Dictionary<string, object>();
        List<ComponentPropertyInfo> properties = GetComponentProperties(propsType);

        foreach (ComponentPropertyInfo prop in properties)
        {
            defaults[prop.Name] = prop.DefaultValue ?? GetDefaultValue(prop.Type) ?? new object();
        }

        return defaults;
    }

    private object? GetDefaultValue(Type type)
    {
        if (type.IsEnum)
        {
            Array enumValues = Enum.GetValues(type);
            return enumValues.Length > 0 ? enumValues.GetValue(0) : null;
        }

        if (type == typeof(string))
        {
            return string.Empty;
        }

        if (type == typeof(bool) || type == typeof(bool?))
        {
            return false;
        }

        // For collections, return empty list
        if (IsCollectionType(type))
        {
            if (type.IsGenericType)
            {
                Type elementType = type.GetGenericArguments()[0];
                Type listType = typeof(List<>).MakeGenericType(elementType);
                return Activator.CreateInstance(listType);
            }
            return new List<object>();
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        return null;
    }

    private object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
        {
            return null;
        }

        // Handle nullable types
        Type underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (value.GetType() == underlyingType || value.GetType() == targetType)
        {
            return value;
        }

        // Handle enums
        if (underlyingType.IsEnum)
        {
            if (value is string stringValue)
            {
                return Enum.Parse(underlyingType, stringValue, true);
            }
            return Enum.ToObject(underlyingType, value);
        }

        // Handle strings
        if (underlyingType == typeof(string))
        {
            return value.ToString();
        }

        // Handle booleans
        if (underlyingType == typeof(bool))
        {
            if (value is string boolString)
            {
                return bool.Parse(boolString);
            }

            return Convert.ToBoolean(value);
        }

        // Handle numbers
        if (underlyingType.IsValueType && !IsComplexType(underlyingType))
        {
            return Convert.ChangeType(value, underlyingType);
        }

        // Handle collections and complex types
        if (IsCollectionType(targetType) || IsComplexType(targetType))
        {
            // If the value is already the correct collection type, return it
            if (targetType.IsAssignableFrom(value.GetType()))
            {
                return value;
            }

            // If value is a string, try to deserialize from JSON
            if (value is string jsonString && !string.IsNullOrWhiteSpace(jsonString))
            {
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Converters = { new InterfaceToConcreteConverter() }
                    };
                    return JsonSerializer.Deserialize(jsonString, targetType, options);
                }
                catch (JsonException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to deserialize collection/complex type from JSON: {ex.Message}");
                }
            }

            // If value is JsonElement (from JSON deserialization), convert it
            if (value is JsonElement jsonElement)
            {
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Converters = { new InterfaceToConcreteConverter() }
                    };
                    return JsonSerializer.Deserialize(jsonElement.GetRawText(), targetType, options);
                }
                catch (JsonException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to deserialize JsonElement: {ex.Message}");
                }
            }
        }

        // For complex types, return as-is (they should already be deserialized)
        return value;
    }

    private string GetDisplayName(PropertyInfo property)
    {
        DisplayNameAttribute? displayAttribute = property.GetCustomAttribute<DisplayNameAttribute>();
        return displayAttribute?.DisplayName ?? SplitCamelCase(property.Name);
    }

    private string? GetDescription(PropertyInfo property)
    {
        DescriptionAttribute? descriptionAttribute = property.GetCustomAttribute<DescriptionAttribute>();
        return descriptionAttribute?.Description;
    }

    private string SplitCamelCase(string input)
    {
        return string.Concat(input.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
    }
}
