using Manifesto.RazorKit.Models;

namespace Manifesto.RazorKit.Tests.Components.Atoms.Mutating;

public class MutatingStories : ComponentStoriesBase<MutatingProps>
{
    // Held on the instance on purpose: CreateStory passes this live list into the props, so a
    // story instance that outlived one request would carry the previous request's mutations.
    private readonly List<string> _items = new() { "seed" };

    public override string ComponentName => "Mutating";

    public override List<ComponentStory> GetStories() =>
    [
        CreateStory("default", "Default", "Props share a list with the story", new MutatingProps { Items = _items })
    ];
}
