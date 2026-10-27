using Manifesto.RazorKit.Tests.Components.Molecules.Tile;

namespace Manifesto.RazorKit.Tests.Components.Organisms.Gallery;

public class GalleryProps
{
    public string? Heading { get; set; }

    public List<ITileProps> Tiles { get; set; } = new();
}
