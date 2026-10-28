namespace Manifesto.RazorKit.Tests.Components.Shared;

// An interface whose conventional concrete type is also the type stories use.
public interface IPictureProps
{
    string? Src { get; set; }

    string? Alt { get; set; }
}

public class PictureProps : IPictureProps
{
    public string? Src { get; set; }

    public string? Alt { get; set; }
}
