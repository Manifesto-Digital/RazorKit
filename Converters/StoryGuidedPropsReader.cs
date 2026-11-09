using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Html;

namespace Manifesto.RazorKit.Converters;

/// <summary>
/// Rebuilds a Controls value posted back for one props member, taking the runtime types from the
/// story's own value for that member. A member declared as an interface, <c>object</c> or a base
/// class carries no type in the posted JSON, and the name convention (<c>ILinkProps</c> to
/// <c>LinkProps</c>) cannot know that the story assigned a derived type whose partial declares
/// it, so without this a re-render hands the partial the wrong type.
/// </summary>
/// <remarks>
/// Types come only from the story instance (server code) and the registry's convention. The posted
/// JSON supplies values, never types: a <c>$type</c> or similar member is just an unknown property.
/// Values whose story value needs no type guidance take the existing path unchanged
/// (<see cref="JsonSerializer"/> with <see cref="InterfaceToConcreteConverter"/>).
/// </remarks>
internal sealed class StoryGuidedPropsReader
{
    // The posted document is already capped at this depth by the controller. Story graphs can be
    // cyclic (a child holding its parent), so the walk over them stops here as well. The cap only
    // guarantees the walk ends; the per-request memo is what keeps its cost linear in the graph.
    private const int MaxDepth = 16;

    // Story sequences can be lazy and endless, so no walk over one is unbounded.
    private const int MaxStoryItemsWalked = 1000;

    private readonly JsonSerializerOptions _options;

    public StoryGuidedPropsReader(JsonSerializerOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Throws when the value cannot be rebuilt to the declared type; the caller decides the fallback.
    /// </summary>
    public object? Read(JsonElement json, Type declaredType, object? storyValue) =>
        Rebuild(json, declaredType, storyValue, depth: 0, new Dictionary<(object, Type), bool>(MemoComparer.Instance));

    // The memo is per Read call, so nothing a story mutates between requests is ever served stale.
    private object? Rebuild(JsonElement json, Type declaredType, object? storyValue, int depth, Dictionary<(object, Type), bool> memo)
    {
        if (json.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!NeedsGuidance(declaredType, storyValue, depth, memo))
        {
            return json.Deserialize(declaredType, _options);
        }

        // NeedsGuidance is only true for a non-null story value that the declared type accepts.
        Type storyType = storyValue!.GetType();

        if (json.ValueKind == JsonValueKind.Array)
        {
            // A collection declared as object takes its shape from the story's collection.
            Type shape = TryGetElementType(declaredType, out _) ? declaredType : storyType;
            if (TryGetElementType(shape, out Type elementType) && storyValue is IEnumerable storyItems)
            {
                // One more than the posted length is enough to see whether items share a type; the
                // story's sequence may be endless.
                var storyList = storyItems.Cast<object?>().Take(json.GetArrayLength() + 1).ToList();
                return RebuildCollection(json, shape, elementType, storyList, depth, memo);
            }
        }

        if (json.ValueKind == JsonValueKind.Object && !IsScalar(storyType))
        {
            return IsSettableObject(storyType)
                ? RebuildObject(json, storyType, storyValue, depth, memo)
                : json.Deserialize(storyType, _options);
        }

        // Guidance applies but this posted shape cannot take it. Deserializing to the declared type
        // would hand the partial a JsonElement for a scalar, so let the caller keep the story's value.
        if (json.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            throw new NotSupportedException("A scalar was posted where the story holds a value that needs its own type.");
        }

        return json.Deserialize(declaredType, _options);
    }

    private object RebuildCollection(JsonElement json, Type collectionType, Type elementType, List<object?> storyItems, int depth, Dictionary<(object, Type), bool> memo)
    {
        // An item the person added beyond the story's items takes the type the story's items share;
        // with mixed or no story items there is nothing to take it from.
        object? sharedGuide = storyItems.Count > 0
                              && storyItems.All(i => i != null && i.GetType() == storyItems[0]!.GetType())
            ? storyItems[0]
            : null;

        var items = new List<object?>();
        var index = 0;
        foreach (JsonElement element in json.EnumerateArray())
        {
            object? guide = index < storyItems.Count ? storyItems[index] : sharedGuide;
            index++;

            if (guide == null && elementType == typeof(object) && element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                // Deserialized as object this would be a JsonElement, which no partial accepts.
                throw new NotSupportedException("An object-typed collection item has no story item to take its type from.");
            }

            items.Add(Rebuild(element, elementType, guide, depth + 1, memo));
        }

        return CreateCollection(collectionType, elementType, items);
    }

    private object RebuildObject(JsonElement json, Type targetType, object storyValue, int depth, Dictionary<(object, Type), bool> memo)
    {
        var instance = Activator.CreateInstance(targetType)!;

        foreach (JsonProperty member in json.EnumerateObject())
        {
            PropertyInfo? property = FindSettableProperty(targetType, member.Name);
            if (property == null)
            {
                continue;
            }

            property.SetValue(instance, Rebuild(member.Value, property.PropertyType, property.GetValue(storyValue), depth + 1, memo));
        }

        return instance;
    }

    /// <summary>
    /// True when the story value holds a runtime type the declared type does not name, here or
    /// anywhere below it. Everything else keeps the existing deserialization.
    /// </summary>
    /// <remarks>
    /// Memoised on (instance, declared type) by reference. A node already being walked is recorded
    /// as false first: reaching it again through a cycle adds nothing, and any guidance found
    /// elsewhere still surfaces through the node that started the walk.
    /// </remarks>
    private static bool NeedsGuidance(Type declaredType, object? storyValue, int depth, Dictionary<(object, Type), bool> memo)
    {
        if (storyValue == null || depth > MaxDepth || !declaredType.IsInstanceOfType(storyValue))
        {
            return false;
        }

        Type storyType = storyValue.GetType();
        if (IsScalar(storyType))
        {
            return false;
        }

        if (storyType != declaredType)
        {
            return true;
        }

        if (memo.TryGetValue((storyValue, declaredType), out bool known))
        {
            return known;
        }

        memo[(storyValue, declaredType)] = false;
        bool result = WalkForGuidance(declaredType, storyValue, storyType, depth, memo);
        memo[(storyValue, declaredType)] = result;
        return result;
    }

    private static bool WalkForGuidance(Type declaredType, object storyValue, Type storyType, int depth, Dictionary<(object, Type), bool> memo)
    {
        if (storyValue is IEnumerable items)
        {
            return TryGetElementType(declaredType, out Type elementType)
                   && items.Cast<object?>().Take(MaxStoryItemsWalked).Any(item => NeedsGuidance(elementType, item, depth + 1, memo));
        }

        if (!IsSettableObject(storyType))
        {
            return false;
        }

        foreach (PropertyInfo property in GetSettableProperties(storyType))
        {
            object? memberValue;
            try
            {
                memberValue = property.GetValue(storyValue);
            }
            catch (TargetInvocationException)
            {
                // A getter that throws gives no guidance; the member keeps the existing path.
                continue;
            }

            if (NeedsGuidance(property.PropertyType, memberValue, depth + 1, memo))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class MemoComparer : IEqualityComparer<(object Instance, Type Declared)>
    {
        public static readonly MemoComparer Instance = new();

        public bool Equals((object Instance, Type Declared) x, (object Instance, Type Declared) y) =>
            ReferenceEquals(x.Instance, y.Instance) && x.Declared == y.Declared;

        public int GetHashCode((object Instance, Type Declared) key) =>
            HashCode.Combine(ReferenceEqualityComparer.Instance.GetHashCode(key.Instance), key.Declared);
    }

    private static object CreateCollection(Type collectionType, Type elementType, List<object?> items)
    {
        if (collectionType.IsArray)
        {
            var array = Array.CreateInstance(elementType, items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                array.SetValue(items[i], i);
            }

            return array;
        }

        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        if (!collectionType.IsAssignableFrom(list.GetType()))
        {
            if (collectionType.IsAbstract || collectionType.IsInterface || !typeof(IList).IsAssignableFrom(collectionType)
                || collectionType.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new NotSupportedException("The collection type cannot be created from the posted items.");
            }

            list = (IList)Activator.CreateInstance(collectionType)!;
        }

        foreach (object? item in items)
        {
            list.Add(item);
        }

        return list;
    }

    private static bool TryGetElementType(Type type, out Type elementType)
    {
        elementType = typeof(object);
        if (type == typeof(string) || typeof(IDictionary).IsAssignableFrom(type) || IsGenericDictionary(type))
        {
            return false;
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        Type? enumerable = type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerable != null)
        {
            elementType = enumerable.GetGenericArguments()[0];
            return true;
        }

        return typeof(IEnumerable).IsAssignableFrom(type);
    }

    private static bool IsGenericDictionary(Type type) =>
        type.GetInterfaces().Append(type).Any(i => i.IsGenericType
            && (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));

    private static bool IsScalar(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
        || type == typeof(Guid) || type == typeof(Uri) || typeof(IHtmlContent).IsAssignableFrom(type);

    // A class the reader can build member by member, the way CreateComponentInstance builds the props.
    private static bool IsSettableObject(Type type) =>
        type.IsClass && !type.IsAbstract && type != typeof(object) && !typeof(IEnumerable).IsAssignableFrom(type)
        && type.GetConstructor(Type.EmptyTypes) != null;

    private static IEnumerable<PropertyInfo> GetSettableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.SetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0
                        && p.GetCustomAttribute<JsonIgnoreAttribute>() == null);

    // Matched like the serializer matches with PropertyNameCaseInsensitive, honouring [JsonPropertyName],
    // so the names the Controls textarea was written with read back.
    private static PropertyInfo? FindSettableProperty(Type type, string jsonName) =>
        GetSettableProperties(type).FirstOrDefault(p =>
            string.Equals(p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name, jsonName, StringComparison.OrdinalIgnoreCase));
}
