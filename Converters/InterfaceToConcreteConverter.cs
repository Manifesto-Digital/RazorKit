using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Html;

namespace Manifesto.RazorKit.Converters;

/// <summary>
/// JSON converter that automatically maps interface types to their concrete implementations.
/// Assumes concrete types follow the naming convention: ITypeName -> TypeName
/// </summary>
public class InterfaceToConcreteConverter : JsonConverter<object>
{
    // Cache to avoid repeated reflection lookups
    private static readonly ConcurrentDictionary<Type, Type?> _typeCache = new();

    // Known interface to concrete type mappings for framework types
    private static readonly Dictionary<string, Type> _knownMappings = new()
    {
        { "Microsoft.AspNetCore.Html.IHtmlContent", typeof(Microsoft.AspNetCore.Html.HtmlString) }
    };

    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Only handle interface types
        if (!typeToConvert.IsInterface)
        {
            using var nonInterfaceDoc = JsonDocument.ParseValue(ref reader);
            return JsonSerializer.Deserialize(nonInterfaceDoc.RootElement.GetRawText(), typeToConvert, options);
        }

        // Special handling for IHtmlContent - treat as string and wrap in HtmlString
        if (typeToConvert == typeof(IHtmlContent))
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? htmlString = reader.GetString();
                return htmlString != null ? new HtmlString(htmlString) : HtmlString.Empty;
            }
            else if (reader.TokenType == JsonTokenType.Null)
            {
                return HtmlString.Empty;
            }
            throw new JsonException($"Expected string value for IHtmlContent, got {reader.TokenType}");
        }

        Type? concreteType = GetConcreteType(typeToConvert) ?? throw new JsonException($"Cannot find concrete type for interface: {typeToConvert.FullName}");

        // Read the JSON as a document first to avoid reader state issues
        using var doc = JsonDocument.ParseValue(ref reader);
        var rawJson = doc.RootElement.GetRawText();

        // Create options without this converter to avoid infinite recursion
        JsonSerializerOptions newOptions = CreateOptionsWithoutThisConverter(options);

        // Deserialize to the concrete type
        return JsonSerializer.Deserialize(rawJson, concreteType, newOptions);
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsInterface;
    }

    private static Type? GetConcreteType(Type interfaceType)
    {
        if (!interfaceType.IsInterface)
        {
            return null;
        }

        // Check cache first
        return _typeCache.GetOrAdd(interfaceType, FindConcreteType);
    }

    private static Type? FindConcreteType(Type interfaceType)
    {
        // Check known mappings first (for framework types like IHtmlContent)
        if (_knownMappings.TryGetValue(interfaceType.FullName ?? string.Empty, out Type? knownType))
        {
            return knownType;
        }

        var interfaceName = interfaceType.Name;

        // Check naming convention: ITypeName -> TypeName
        if (!interfaceName.StartsWith("I") || interfaceName.Length <= 1)
        {
            return null;
        }

        var concreteTypeName = $"{interfaceType.Namespace}.{interfaceName[1..]}";

        // Search in loaded assemblies (skip dynamic assemblies for performance)
        IEnumerable<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.FullName));

        foreach (Assembly? assembly in assemblies)
        {
            try
            {
                Type? concreteType = assembly.GetType(concreteTypeName, throwOnError: false);

                // Verify the type implements the interface
                if (concreteType != null && interfaceType.IsAssignableFrom(concreteType) && !concreteType.IsAbstract)
                {
                    return concreteType;
                }
            }
            catch
            {
                // Silently skip assemblies that can't be scanned
            }
        }

        return null;
    }

    private static JsonSerializerOptions CreateOptionsWithoutThisConverter(JsonSerializerOptions original)
    {
        var newOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = original.PropertyNameCaseInsensitive,
            PropertyNamingPolicy = original.PropertyNamingPolicy,
            DefaultIgnoreCondition = original.DefaultIgnoreCondition,
            WriteIndented = original.WriteIndented,
            ReferenceHandler = original.ReferenceHandler,
            MaxDepth = original.MaxDepth,
            AllowTrailingCommas = original.AllowTrailingCommas,
            ReadCommentHandling = original.ReadCommentHandling
        };

        // Copy converters except this one
        foreach (JsonConverter converter in original.Converters)
        {
            if (converter.GetType() != typeof(InterfaceToConcreteConverter))
            {
                newOptions.Converters.Add(converter);
            }
        }

        return newOptions;
    }
}
