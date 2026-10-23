namespace Manifesto.RazorKit.Tests.Components.Atoms.Exploding;

public class ExplodingProps
{
    // A ref struct cannot be instantiated by reflection, so RazorKit's property inspection
    // throws for this props type. That gives a request that fails outside the controller's own
    // catch blocks, which is what the host's exception handler has to render.
    public Span<int> Items => default;
}
