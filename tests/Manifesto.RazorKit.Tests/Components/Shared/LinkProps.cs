namespace Manifesto.RazorKit.Tests.Components.Shared;

// Mirrors a consumer shape: props members typed as this interface, while stories assign the
// derived ActionProps, whose partial declares the derived type. The name convention alone
// (ILinkProps -> LinkProps) picks the base class, which that partial rejects.
public interface ILinkProps
{
    string? Text { get; set; }

    string? Url { get; set; }
}

public class LinkProps : ILinkProps
{
    public string? Text { get; set; }

    public string? Url { get; set; }
}

public enum ActionStyle
{
    Primary,
    Secondary
}

public class ActionProps : LinkProps
{
    public ActionStyle Style { get; set; }
}
