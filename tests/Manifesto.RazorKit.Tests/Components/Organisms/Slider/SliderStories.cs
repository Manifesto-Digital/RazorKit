using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Tests.Components.Molecules.Tile;
using Manifesto.RazorKit.Tests.Components.Shared;

namespace Manifesto.RazorKit.Tests.Components.Organisms.Slider;

public class SliderStories : ComponentStoriesBase<SliderProps>
{
    public override string ComponentName => "Slider";

    public override List<ComponentStory> GetStories() =>
    [
        CreateStory("Basic", "Basic", "Tiles in an object list", new SliderProps
        {
            ItemTemplatePath = "~/Components/Molecules/Tile/Tile.cshtml",
            Items =
            [
                new TileProps { Title = "Slide one", Link = new ActionProps { Text = "One", Url = "/one" } },
                new TileProps { Title = "Slide two" }
            ]
        })
    ];
}
