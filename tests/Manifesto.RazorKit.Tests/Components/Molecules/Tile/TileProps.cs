using Manifesto.RazorKit.Tests.Components.Shared;

namespace Manifesto.RazorKit.Tests.Components.Molecules.Tile;

public interface ITileProps
{
    string? Title { get; set; }

    ILinkProps? Link { get; set; }
}

public class TileProps : ITileProps
{
    public string? Title { get; set; }

    public ILinkProps? Link { get; set; }
}
