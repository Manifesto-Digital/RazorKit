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

    // Page only. After the headers filter, so the 405 still carries no-store.
    public const int MethodCheck = -9_000;

    // After the method check, before request protection: a request over its limit is turned away
    // before any further per-request work (such as reading the body) is done for it.
    public const int RateLimit = -8_000;

    // A resource filter, so it runs before model binding reads the body; after the headers
    // filter, so its 403, 413 and 415 responses still carry no-store.
    public const int RequestProtection = -7_000;

    // After request protection, so a request that fails it, or whose body is still being read,
    // never holds one of the few render slots.
    public const int ConcurrencyLimit = -6_000;
}
