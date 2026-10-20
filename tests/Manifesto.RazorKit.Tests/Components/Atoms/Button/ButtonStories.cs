using Manifesto.RazorKit.Models;

namespace Manifesto.RazorKit.Tests.Components.Atoms.Button;

public class ButtonStories : ComponentStoriesBase<ButtonProps>
{
    public override string ComponentName => "Button";

    public override List<ComponentStory> GetStories() =>
    [
        CreateStory("default", "Default", "A default button", new ButtonProps())
    ];
}
