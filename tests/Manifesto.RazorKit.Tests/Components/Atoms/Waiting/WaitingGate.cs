namespace Manifesto.RazorKit.Tests.Components.Atoms.Waiting;

/// <summary>
/// Lets a test hold a render open. Disarmed by default, so the component renders instantly
/// for every other test. Process-wide state: every test class that arms it is in
/// <see cref="Manifesto.RazorKit.Tests.WaitingGateCollection"/>, which xUnit never runs in parallel.
/// </summary>
public static class WaitingGate
{
    private static TaskCompletionSource _release = NewSignal();
    private static TaskCompletionSource _entered = NewSignal();
    private static volatile bool _armed;

    public static void Arm()
    {
        _release = NewSignal();
        _entered = NewSignal();
        _armed = true;
    }

    public static void Release()
    {
        _armed = false;
        _release.TrySetResult();
    }

    public static Task Entered => _entered.Task;

    public static async Task PassAsync()
    {
        if (!_armed)
        {
            return;
        }

        _entered.TrySetResult();
        await _release.Task;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
