using System.Reflection;
using System.Text.Json;
using Manifesto.RazorKit.Converters;
using Microsoft.AspNetCore.Html;

namespace Manifesto.RazorKit.Helpers;

/// <summary>
/// Handles deserialization of JSON properties into strongly-typed objects
/// </summary>
public static class PropertyDeserializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new InterfaceToConcreteConverter() }
    };

    /// <summary>
    /// Deserializes JSON element to a dictionary of property values matching the target type
    /// </summary>
    public static Dictionary<string, object> DeserializePropsFromJson(JsonElement jsonElement, Type propsType)
    {
        var propertyValues = new Dictionary<string, object>();

        try
        {
            foreach (JsonProperty property in jsonElement.EnumerateObject())
            {
                var propInfo = FindPropertyInfo(propsType, property.Name);
                if (propInfo == null) continue;

                var value = DeserializeProperty(property.Value, propInfo);
                if (value != null)
                {
                    propertyValues[property.Name] = value;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deserializing props: {ex.Message}");
        }

        return propertyValues;
    }

    private static PropertyInfo? FindPropertyInfo(Type propsType, string propertyName)
    {
        return propsType.GetProperty(
            propertyName, 
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
        );
    }

    private static object? DeserializeProperty(JsonElement jsonValue, PropertyInfo propInfo)
    {
        return jsonValue.ValueKind switch
        {
            JsonValueKind.String => DeserializeString(jsonValue, propInfo),
            JsonValueKind.Number => DeserializeNumber(jsonValue, propInfo),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array or JsonValueKind.Object => DeserializeComplexType(jsonValue, propInfo),
            _ => null
        };
    }

    private static object? DeserializeString(JsonElement jsonValue, PropertyInfo propInfo)
    {
        var stringValue = jsonValue.GetString();
        
        // Special handling for IHtmlContent
        if (propInfo.PropertyType == typeof(IHtmlContent))
        {
            return new HtmlString(stringValue ?? string.Empty);
        }
        
        return stringValue;
    }

    private static object? DeserializeNumber(JsonElement jsonValue, PropertyInfo propInfo)
    {
        var targetType = propInfo.PropertyType;
        
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        
        return underlyingType.Name switch
        {
            nameof(Int32) => jsonValue.GetInt32(),
            nameof(Decimal) => jsonValue.GetDecimal(),
            nameof(Double) => jsonValue.GetDouble(),
            nameof(Int64) => jsonValue.GetInt64(),
            nameof(Single) => jsonValue.GetSingle(),
            _ => jsonValue.GetInt32() // Default to int
        };
    }

    private static object? DeserializeComplexType(JsonElement jsonValue, PropertyInfo propInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(
                jsonValue.GetRawText(), 
                propInfo.PropertyType, 
                JsonOptions
            );
        }
        catch (JsonException jsonEx)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Error deserializing property {propInfo.Name}: {jsonEx.Message}"
            );
            return null;
        }
    }
}
