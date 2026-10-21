using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Manifesto.RazorKit.Models;
using Microsoft.Extensions.Logging;

namespace Manifesto.RazorKit.Filters;

internal enum RateLimitBucket
{
    Page,
    Render
}

internal readonly record struct RateLimitDecision(bool Allowed, int RetryAfterSeconds);

/// <summary>
/// RazorKit's own limiter state: a token bucket per client for page loads and for renders, plus one
/// limit on renders in flight across all clients. Built on System.Threading.RateLimiting, which
/// is part of the shared framework, and not on the host's <c>UseRateLimiter()</c>, which a host
/// may not have configured.
/// </summary>
internal sealed class RazorKitRateLimiter : IDisposable
{
    // Where connections with no remote address end up. Cannot collide with an IP address string.
    internal const string UnknownPartition = "unknown";

    private static readonly TimeSpan LogInterval = TimeSpan.FromMinutes(1);

    // Bounds the log bookkeeping, not the limiting itself: a flood from more partitions than this
    // cannot grow memory or the log. Beyond it, rejections are counted and summarised.
    internal const int MaxTrackedLogKeys = 10_000;

    private readonly PartitionedRateLimiter<string> _page;
    private readonly PartitionedRateLimiter<string> _render;
    private readonly ConcurrencyLimiter _concurrency;
    private readonly ILogger<RazorKitRateLimiter> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, long> _lastLogged = new();
    private readonly byte[] _tagKey = RandomNumberGenerator.GetBytes(32);
    private int _trackedLogKeys;
    private long _suppressedRejections;
    private long _nextMaintenance;

    public RazorKitRateLimiter(RazorKitOptions options, ILogger<RazorKitRateLimiter> logger)
        : this(options, logger, TimeProvider.System)
    {
    }

    internal RazorKitRateLimiter(RazorKitOptions options, ILogger<RazorKitRateLimiter> logger, TimeProvider time)
    {
        _logger = logger;
        _time = time;
        _nextMaintenance = time.GetTimestamp() + ToTimestamp(time, LogInterval);
        _page = CreateBucketLimiter(options.RateLimit.Page);
        _render = CreateBucketLimiter(options.RateLimit.Render);
        _concurrency = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = options.RateLimit.MaxConcurrentRenders,
            // No queue: a saturated backstop answers 503 straight away instead of tying up more requests.
            QueueLimit = 0
        });
    }

    /// <summary>
    /// The partition a client address belongs to. The address is the only input: nothing a client
    /// sends in a header is consulted.
    /// </summary>
    internal static string GetPartitionKey(IPAddress? address)
    {
        if (address is null)
        {
            return UnknownPartition;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        // A single host usually controls its whole /64, so grouping stops it from stepping through addresses.
        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();
        return new IPAddress(bytes).ToString() + "/64";
    }

    public RateLimitDecision TryTakeToken(RateLimitBucket bucket, IPAddress? clientAddress)
    {
        var key = GetPartitionKey(clientAddress);
        var limiter = bucket == RateLimitBucket.Render ? _render : _page;

        using var lease = limiter.AttemptAcquire(key);
        if (lease.IsAcquired)
        {
            return new RateLimitDecision(true, 0);
        }

        LogRejection(bucket, key);
        return new RateLimitDecision(false, RetryAfterSeconds(lease));
    }

    /// <summary>
    /// Takes one of the shared render slots. Dispose the lease when the render ends. Check
    /// <see cref="RateLimitLease.IsAcquired"/>: a saturated limiter returns a lease that is not acquired.
    /// </summary>
    public RateLimitLease TryAcquireRenderSlot() => _concurrency.AttemptAcquire(1);

    public void Dispose()
    {
        _page.Dispose();
        _render.Dispose();
        _concurrency.Dispose();
    }

    private static PartitionedRateLimiter<string> CreateBucketLimiter(RazorKitBucketOptions bucket) =>
        PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = bucket.Burst,
                // One token at a time at an even pace, so the per-minute rate is spread across the
                // minute instead of arriving as one refill.
                TokensPerPeriod = 1,
                ReplenishmentPeriod = TimeSpan.FromMilliseconds(Math.Max(1, 60_000.0 / bucket.PermitsPerMinute)),
                AutoReplenishment = true,
                QueueLimit = 0
            }));

    private static int RetryAfterSeconds(RateLimitLease lease) =>
        lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : 1;

    internal int TrackedLogKeyCount => Volatile.Read(ref _trackedLogKeys);

    // At most one entry per partition per interval, so a client hammering a closed bucket cannot
    // fill the log; and bounded in the number of partitions it remembers, so a flood from many
    // addresses cannot grow memory.
    private void LogRejection(RateLimitBucket bucket, string partition)
    {
        var now = _time.GetTimestamp();
        RunMaintenanceIfDue(now);

        var logKey = $"{bucket}|{partition}";
        while (true)
        {
            if (_lastLogged.TryGetValue(logKey, out var last))
            {
                if (_time.GetElapsedTime(last, now) < LogInterval)
                {
                    return;
                }

                if (_lastLogged.TryUpdate(logKey, now, last))
                {
                    break;
                }

                continue;
            }

            if (Volatile.Read(ref _trackedLogKeys) >= MaxTrackedLogKeys)
            {
                Interlocked.Increment(ref _suppressedRejections);
                return;
            }

            if (_lastLogged.TryAdd(logKey, now))
            {
                Interlocked.Increment(ref _trackedLogKeys);
                break;
            }
        }

        _logger.LogWarning(
            "RazorKit rate limit reached for the {Bucket} bucket, client partition {Partition}. Logged at most once a minute per partition.",
            bucket, PartitionTag(partition));
    }

    // Once per interval: forget partitions not seen rejected for a full interval, and report how
    // many rejections went unlogged because tracking was full.
    private void RunMaintenanceIfDue(long now)
    {
        var due = Volatile.Read(ref _nextMaintenance);
        if (now < due ||
            Interlocked.CompareExchange(ref _nextMaintenance, now + ToTimestamp(_time, LogInterval), due) != due)
        {
            return;
        }

        foreach (var entry in _lastLogged)
        {
            if (_time.GetElapsedTime(entry.Value, now) >= LogInterval && _lastLogged.TryRemove(entry))
            {
                Interlocked.Decrement(ref _trackedLogKeys);
            }
        }

        var suppressed = Interlocked.Exchange(ref _suppressedRejections, 0);
        if (suppressed > 0)
        {
            _logger.LogWarning(
                "RazorKit rate limiting rejected {Count} further requests in the last minute from partitions that were not logged individually.",
                suppressed);
        }
    }

    // A tag, not the address: lets lines from one process be matched to each other without naming
    // anyone. The key is random per process, so tags cannot be reversed or compared across restarts.
    private string PartitionTag(string partition) =>
        Convert.ToHexString(HMACSHA256.HashData(_tagKey, Encoding.UTF8.GetBytes(partition)).AsSpan(0, 4)).ToLowerInvariant();

    private static long ToTimestamp(TimeProvider time, TimeSpan span) =>
        (long)(span.TotalSeconds * time.TimestampFrequency);
}
