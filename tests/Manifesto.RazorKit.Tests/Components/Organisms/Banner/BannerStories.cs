using Manifesto.RazorKit.Models;
using Manifesto.RazorKit.Tests.Components.Shared;

namespace Manifesto.RazorKit.Tests.Components.Organisms.Banner;

public class BannerStories : ComponentStoriesBase<BannerProps>
{
    public override string ComponentName => "Banner";

    public override List<ComponentStory> GetStories() =>
    [
        CreateStory("Default", "Default", "Every prop", new BannerProps
        {
            Eyebrow = "Eyebrow",
            Title = "Banner title",
            Action = new ActionProps { Text = "Story action", Url = "/story", Style = ActionStyle.Secondary },
            Picture = new PictureProps { Src = "/story.png", Alt = "Story picture" }
        }),
        CreateStory("No Picture", "No picture", "Every prop but the picture", new BannerProps
        {
            Title = "Banner title",
            Action = new ActionProps { Text = "Story action", Url = "/story", Style = ActionStyle.Secondary }
        }),
        CreateStory("Title Only", "Title only", "Only a title", new BannerProps { Title = "Banner title" })
    ];
}
