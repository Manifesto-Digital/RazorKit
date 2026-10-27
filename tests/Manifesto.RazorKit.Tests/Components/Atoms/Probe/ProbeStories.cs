using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Tests.Components.Shared;

namespace Manifesto.RazorKit.Tests.Components.Atoms.Probe;

public class ProbeStories : ComponentStoriesBase<ProbeProps>
{
    public override string ComponentName => "Probe";

    public override List<ComponentStory> GetStories() =>
    [
        CreateStory("Cyclic", "Cyclic", "A fully connected story graph with parent back-references", new ProbeProps { Root = CyclicGraph() }),
        CreateStory("Infinite", "Infinite", "A lazy story collection that never ends", new ProbeProps { Links = Endless() }),
        CreateStory("Unserialisable", "Unserialisable", "A story value holding a Type member",
            new ProbeProps { Payload = new ProbeUnserialisable { Name = "x", Kind = typeof(string) } }),
        CreateStory("NullGetter", "Null getter", "A story value whose getter throws NullReferenceException",
            new ProbeProps { Title = "Still here", Payload = new ProbeNullReferenceGetter() }),
        CreateStory("ThrowingGetter", "Throwing getter", "A story value whose getter throws InvalidOperationException",
            new ProbeProps { Title = "Still here", Payload = new ProbeInvalidOperationGetter() }),
        CreateStory("Guided", "Guided", "An interface-typed member holding a derived type",
            new ProbeProps { Action = new ActionProps { Text = "Story action", Url = "/story" } })
    ];

    // Every node lists every other node as a child and points back at the first one: the depth
    // cap guarantees a walk ends, but not that it ends soon.
    private static ProbeNode CyclicGraph()
    {
        var nodes = Enumerable.Range(0, 10).Select(i => new ProbeNode { Name = "n" + i }).ToList();
        foreach (ProbeNode node in nodes)
        {
            node.Parent = nodes[0];
            node.Children = nodes.Where(n => n != node).ToList();
        }

        return nodes[0];
    }

    private static IEnumerable<LinkItem> Endless()
    {
        while (true)
        {
            yield return new LinkItem { Text = "again" };
        }
    }
}
