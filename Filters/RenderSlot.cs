using System.Threading.RateLimiting;

namespace Manifesto.RazorKit.Filters;

/// <summary>
/// A render slot that can be given back from two places, when the response starts and when the
/// request ends, without either releasing it twice.
/// </summary>
internal sealed class RenderSlot : IDisposable
{
    private RateLimitLease? _lease;

    public RenderSlot(RateLimitLease lease)
    {
        _lease = lease;
    }

    public bool IsAcquired => _lease?.IsAcquired ?? false;

    public void Dispose() => Interlocked.Exchange(ref _lease, null)?.Dispose();
}
