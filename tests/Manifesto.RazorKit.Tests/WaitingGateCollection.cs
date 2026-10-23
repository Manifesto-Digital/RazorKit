namespace Manifesto.RazorKit.Tests;

/// <summary>
/// Every test class that arms <c>WaitingGate</c> belongs here. The gate is process-wide state, so
/// those classes must not run in parallel with each other or with anything else.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WaitingGateCollection
{
    public const string Name = "WaitingGate";
}
