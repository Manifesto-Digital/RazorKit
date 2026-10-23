namespace Manifesto.RazorKit.Tests.Fixtures;

public interface IFixtureThing
{
    string Title { get; }
}

public class FixtureThing : IFixtureThing
{
    public string Title { get; set; } = string.Empty;
}

public class FixtureThingHolder
{
    public IFixtureThing? Thing { get; set; }
}
