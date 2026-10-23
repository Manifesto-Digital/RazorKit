using Manifesto.RazorKit.Models;

namespace Manifesto.RazorKit.Tests.Components.Atoms.Waiting;

public class WaitingStories : ComponentStoriesBase<WaitingProps>
{
    public override string ComponentName => "Waiting";

    public override List<ComponentStory> GetStories() =>
    [
        CreateStory("default", "Default", "Blocks while rendering when the gate is armed", new WaitingProps())
    ];
}
