namespace Manifesto.RazorKit.Tests.Components.Atoms.Probe;

public class ProbeNode
{
    public string? Name { get; set; }

    public List<ProbeNode> Children { get; set; } = new();

    public ProbeNode? Parent { get; set; }
}

// Holds members no serializer can write, standing in for a consumer value object.
public class ProbeUnserialisable
{
    public string? Name { get; set; }

    public Type? Kind { get; set; }
}

// Getters that throw something other than a serializer exception, standing in for a consumer value
// object whose property reads a service or a null dependency.
public class ProbeNullReferenceGetter
{
    public string? Name { get; set; } = "x";

    public string Broken => throw new NullReferenceException();
}

public class ProbeInvalidOperationGetter
{
    public string? Name { get; set; } = "x";

    public string Broken => throw new InvalidOperationException();
}

// Members typed as an interface or object, so their runtime type comes from the story.
public class ProbeProps
{
    public ProbeNode? Root { get; set; }

    public IEnumerable<LinkItem>? Links { get; set; }

    public object? Payload { get; set; }

    public Shared.ILinkProps? Action { get; set; }

    public string? Title { get; set; }
}

public class LinkItem
{
    public string? Text { get; set; }
}
