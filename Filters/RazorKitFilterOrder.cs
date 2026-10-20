namespace Manifesto.RazorKit.Filters;

/// <summary>
/// Filter order values for RazorKit's own filters. They sit far below MVC's built-in
/// order values (900 and 1000) so ordering never depends on filters a host adds globally.
/// Lowest runs first.
/// </summary>
internal static class RazorKitFilterOrder
{
    // Outermost, so every later short-circuit response (and any exception) still gets the headers.
    public const int ResponseHeaders = -10_000;
}
