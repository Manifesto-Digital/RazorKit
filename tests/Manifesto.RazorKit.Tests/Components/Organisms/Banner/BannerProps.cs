using Manifesto.RazorKit.Tests.Components.Shared;

namespace Manifesto.RazorKit.Tests.Components.Organisms.Banner;

public class BannerProps
{
    public string? Eyebrow { get; set; }

    public string? Title { get; set; }

    public ILinkProps? Action { get; set; }

    public IPictureProps? Picture { get; set; }
}
