using Manifesto.RazorKit.Models;

namespace Manifesto.RazorKit.Tests.Components.Atoms.Throwing;

public class ThrowingStories : ComponentStoriesBase<ThrowingProps>
{
    public override string ComponentName => "Throwing";

    public override List<ComponentStory> GetStories() =>
    [
        CreateStory("default", "Default", "A view that always throws", new ThrowingProps())
    ];
}
