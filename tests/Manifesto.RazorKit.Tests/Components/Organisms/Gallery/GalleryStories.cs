using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Tests.Components.Molecules.Tile;
using Manifesto.RazorKit.Tests.Components.Shared;

namespace Manifesto.RazorKit.Tests.Components.Organisms.Gallery;

public class GalleryStories : ComponentStoriesBase<GalleryProps>
{
    public override string ComponentName => "Gallery";

    // A list of an interface type whose items nest another interface-typed member.
    public override List<ComponentStory> GetStories() =>
    [
        CreateStory("Default", "Default", "Tiles with nested actions", new GalleryProps
        {
            Heading = "Gallery heading",
            Tiles =
            [
                new TileProps { Title = "Tile one", Link = new ActionProps { Text = "Open one", Url = "/one", Style = ActionStyle.Secondary } },
                new TileProps { Title = "Tile two", Link = new ActionProps { Text = "Open two", Url = "/two" } }
            ]
        })
    ];
}
