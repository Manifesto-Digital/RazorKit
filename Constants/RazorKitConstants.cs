namespace Manifesto.RazorKit.Constants;

/// <summary>
/// Constants used throughout RazorKit
/// </summary>
public static class RazorKitConstants
{
    /// <summary>
    /// Route paths
    /// </summary>
    public static class Routes
    {
        public const string PreviewBase = "razorkit-preview";
        public const string PreviewAction = "preview/{componentName}/{storyName?}";
        public const string MainPage = "/razorkit";
    }

    /// <summary>
    /// Default values
    /// </summary>
    public static class Defaults
    {
        public const string StoryName = "default";
        public const string DefaultTheme = "charity";
    }

    /// <summary>
    /// External CDN URLs
    /// </summary>
    public static class ExternalScripts
    {
        public const string AxeCore = "https://unpkg.com/axe-core@4.8.2/axe.min.js";
    }

    /// <summary>
    /// Component file naming conventions
    /// </summary>
    public static class ComponentNaming
    {
        public const string PropsSuffix = "Props";
        public const string StoriesSuffix = "Stories";
        public const string ComponentsNamespace = "Components";
    }

    /// <summary>
    /// Atomic design levels
    /// </summary>
    public static class AtomicLevels
    {
        public const string Atoms = "Atoms";
        public const string Molecules = "Molecules";
        public const string Organisms = "Organisms";
        public const string Templates = "Templates";
        public const string Pages = "Pages";
        public const string Unknown = "Unknown";
    }

    /// <summary>
    /// Error messages
    /// </summary>
    public static class ErrorMessages
    {
        public const string ComponentNotFound = "Component not found";
        public const string PropsTypeNotFound = "Props type not found";
        public const string ViewNotFound = "View not found";
        public const string InvalidJson = "Invalid JSON";
    }
}
