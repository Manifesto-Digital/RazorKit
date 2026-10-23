namespace Manifesto.RazorKit.Tests.Components.Atoms.Typed;

public enum TypedMode
{
    Compact,
    Wide
}

public class TypedProps
{
    public string Label { get; set; } = "Typed default label";

    public int Count { get; set; }

    public bool Enabled { get; set; }

    public TypedMode Mode { get; set; }
}
