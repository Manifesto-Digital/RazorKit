using Manifesto.RazorKit.Models;

namespace Manifesto.RazorKit.Tests.Components.Atoms.Large;

public class LargeStories : ComponentStoriesBase<LargeProps>
{
    public override string ComponentName => "Large";

    public override List<ComponentStory> GetStories() =>
    [
        CreateStory("default", "Default", "Renders far more than a response buffer holds", new LargeProps())
    ];
}
