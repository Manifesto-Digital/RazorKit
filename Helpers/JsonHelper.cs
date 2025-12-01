using System.Text.Json;
using Manifesto.RazorKit.Converters;

namespace Manifesto.RazorKit.Helpers;

/// <summary>
/// Provides shared JSON serialization configuration for RazorKit components
/// </summary>
public static class JsonHelper
{
    /// <summary>
    /// Creates JsonSerializerOptions configured for deserializing component props,
    /// including support for interface-to-concrete type mapping
    /// </summary>
    public static JsonSerializerOptions CreateRazorKitOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new InterfaceToConcreteConverter() }
        };
    }
}
