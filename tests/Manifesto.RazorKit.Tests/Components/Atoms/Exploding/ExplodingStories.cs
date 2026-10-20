using Manifesto.RazorKit.Models;

namespace Manifesto.RazorKit.Tests.Components.Atoms.Exploding;

public class ExplodingStories : IComponentStories
{
    public string ComponentName => "Exploding";

    public List<ComponentStory> GetStories() =>
    [
        new ComponentStory { Name = "default", DisplayName = "Default" }
    ];
}
