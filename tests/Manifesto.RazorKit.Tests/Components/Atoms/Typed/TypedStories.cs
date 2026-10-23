using Manifesto.RazorKit.Models;

namespace Manifesto.RazorKit.Tests.Components.Atoms.Typed;

public class TypedStories : ComponentStoriesBase<TypedProps>
{
    public override string ComponentName => "Typed";

    public override List<ComponentStory> GetStories() =>
    [
        CreateStory("default", "Default", "Non-string properties", new TypedProps())
    ];
}
