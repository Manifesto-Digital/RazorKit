namespace Manifesto.RazorKit.Tests.Components.Organisms.Slider;

// Items is object-typed on purpose: the consumer shape renders each item through a partial
// chosen by ItemTemplatePath, so only the story knows what each item really is.
public class SliderProps
{
    public List<object> Items { get; set; } = new();

    public string? ItemTemplatePath { get; set; }

    public bool ShowArrows { get; set; } = true;
}
