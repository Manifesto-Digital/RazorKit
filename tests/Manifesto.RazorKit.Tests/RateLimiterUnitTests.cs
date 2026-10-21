using System.Net;
using Manifesto.RazorKit.Extensions;
using Manifesto.RazorKit.Filters;
using Manifesto.RazorKit.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Manifesto.RazorKit.Tests;

public class RateLimiterUnitTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:3::1", "2001:db8:1:3::/64")]
    public void Partition_key_normalises_mapped_ipv4_and_groups_ipv6_by_slash_64(string address, string expected)
    {
        Assert.Equal(expected, RazorKitRateLimiter.GetPartitionKey(IPAddress.Parse(address)));
    }

    [Fact]
    public void A_null_address_gets_the_shared_unknown_partition()
    {
        Assert.Equal(RazorKitRateLimiter.UnknownPartition, RazorKitRateLimiter.GetPartitionKey(null));
    }

    [Fact]
    public void Retry_After_reflects_the_time_until_the_next_token()
    {
        var options = new RazorKitOptions();
        options.RateLimit.Page.Burst = 1;
        options.RateLimit.Page.PermitsPerMinute = 1; // One token a minute.
        using var limiter = new RazorKitRateLimiter(options, NullLogger<RazorKitRateLimiter>.Instance);
        var client = IPAddress.Parse("203.0.113.1");

        Assert.True(limiter.TryTakeToken(RateLimitBucket.Page, client).Allowed);
        var rejected = limiter.TryTakeToken(RateLimitBucket.Page, client);

        Assert.False(rejected.Allowed);
        Assert.InRange(rejected.RetryAfterSeconds, 1, 60);
    }

    [Fact]
    public void Rejections_are_logged_once_per_partition_per_minute()
    {
        var options = new RazorKitOptions();
        options.RateLimit.Render.Burst = 1;
        options.RateLimit.Render.PermitsPerMinute = 1;
        var log = new CapturingLogger();
        var clock = new ManualTimeProvider();
        using var limiter = new RazorKitRateLimiter(options, log, clock);
        var noisy = IPAddress.Parse("203.0.113.1");
        var other = IPAddress.Parse("203.0.113.2");

        limiter.TryTakeToken(RateLimitBucket.Render, noisy); // spends the token
        for (var i = 0; i < 50; i++)
        {
            limiter.TryTakeToken(RateLimitBucket.Render, noisy);
        }

        Assert.Equal(1, log.Count);

        limiter.TryTakeToken(RateLimitBucket.Render, other); // spends other's token
        limiter.TryTakeToken(RateLimitBucket.Render, other);
        Assert.Equal(2, log.Count);

        clock.Advance(TimeSpan.FromSeconds(61));
        limiter.TryTakeToken(RateLimitBucket.Render, noisy);
        Assert.Equal(3, log.Count);
    }

    [Fact]
    public void Rejection_logs_never_contain_the_client_address_but_correlate_within_the_process()
    {
        var options = new RazorKitOptions();
        options.RateLimit.Render.Burst = 1;
        options.RateLimit.Render.PermitsPerMinute = 1;
        var log = new CapturingLogger();
        var clock = new ManualTimeProvider();
        using var limiter = new RazorKitRateLimiter(options, log, clock);
        var v4 = IPAddress.Parse("203.0.113.77");
        var v6 = IPAddress.Parse("2001:db8:77:88::1");

        foreach (var client in new[] { v4, v6 })
        {
            limiter.TryTakeToken(RateLimitBucket.Render, client);
            limiter.TryTakeToken(RateLimitBucket.Render, client);
        }

        clock.Advance(TimeSpan.FromSeconds(61));
        limiter.TryTakeToken(RateLimitBucket.Render, v4);

        var lines = log.Messages.ToArray();
        Assert.Equal(3, lines.Length);
        Assert.All(lines, l =>
        {
            Assert.DoesNotContain("203.0.113", l);
            Assert.DoesNotContain("2001:db8", l);
            Assert.DoesNotContain("/64", l);
        });
        var tags = lines.Select(l => System.Text.RegularExpressions.Regex.Match(l, @"partition ([0-9a-f]{8})\b")).ToArray();
        Assert.All(tags, t => Assert.True(t.Success, "no 8 hex char partition tag"));
        Assert.Equal(tags[0].Groups[1].Value, tags[2].Groups[1].Value); // same client, same tag
        Assert.NotEqual(tags[0].Groups[1].Value, tags[1].Groups[1].Value);
    }

    [Fact]
    public void Rejections_from_many_partitions_produce_bounded_log_lines_and_tracking()
    {
        const int partitions = 20_000;
        var options = new RazorKitOptions();
        options.RateLimit.Render.Burst = 1;
        options.RateLimit.Render.PermitsPerMinute = 1;
        var log = new CapturingLogger();
        var clock = new ManualTimeProvider();
        using var limiter = new RazorKitRateLimiter(options, log, clock);

        for (var i = 0; i < partitions; i++)
        {
            var client = IPAddress.Parse($"10.{i >> 16}.{(i >> 8) & 255}.{i & 255}");
            limiter.TryTakeToken(RateLimitBucket.Render, client); // spends the token
            limiter.TryTakeToken(RateLimitBucket.Render, client); // rejected
        }

        Assert.True(limiter.TrackedLogKeyCount <= RazorKitRateLimiter.MaxTrackedLogKeys);
        Assert.True(log.Count <= RazorKitRateLimiter.MaxTrackedLogKeys, $"{log.Count} lines");

        // A minute later, one summary line accounts for what was not logged individually, and
        // the stale tracking entries are gone.
        clock.Advance(TimeSpan.FromSeconds(61));
        limiter.TryTakeToken(RateLimitBucket.Render, IPAddress.Parse("192.0.2.1"));
        limiter.TryTakeToken(RateLimitBucket.Render, IPAddress.Parse("192.0.2.1"));

        Assert.Single(log.Messages, m => m.Contains("not logged individually"));
        Assert.True(limiter.TrackedLogKeyCount <= 1);
    }

    [Fact]
    public void The_concurrency_limit_is_shared_across_clients_and_has_no_queue()
    {
        var options = new RazorKitOptions();
        options.RateLimit.MaxConcurrentRenders = 2;
        using var limiter = new RazorKitRateLimiter(options, NullLogger<RazorKitRateLimiter>.Instance);

        using var first = limiter.TryAcquireRenderSlot();
        using var second = limiter.TryAcquireRenderSlot();
        using var third = limiter.TryAcquireRenderSlot();

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);
    }

    [Fact]
    public void Defaults_match_the_documented_limits()
    {
        var limits = new RazorKitOptions().RateLimit;

        Assert.Equal((120, 30), (limits.Render.PermitsPerMinute, limits.Render.Burst));
        Assert.Equal((60, 20), (limits.Page.PermitsPerMinute, limits.Page.Burst));
        Assert.Equal(2 * Environment.ProcessorCount, limits.MaxConcurrentRenders);
    }

    [Theory]
    [InlineData(0, 5, 1)]
    [InlineData(5, 0, 1)]
    [InlineData(5, 5, 0)]
    public void Invalid_limits_fail_at_startup(int permitsPerMinute, int burst, int maxConcurrent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddRazorKit(o =>
            {
                o.RateLimit.Render.PermitsPerMinute = permitsPerMinute;
                o.RateLimit.Render.Burst = burst;
                o.RateLimit.MaxConcurrentRenders = maxConcurrent;
            }));
    }

    [Fact]
    public void Rate_limit_attributes_throw_when_their_filters_are_not_registered()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => new RateLimitAttribute().CreateInstance(services));
        Assert.Throws<InvalidOperationException>(() => new ConcurrencyLimitAttribute().CreateInstance(services));
    }

    [Fact]
    public async Task Disposing_the_render_slot_twice_releases_it_once()
    {
        var options = new RazorKitOptions();
        options.RateLimit.MaxConcurrentRenders = 1;
        using var limiter = new RazorKitRateLimiter(options, NullLogger<RazorKitRateLimiter>.Instance);

        var slot = new RenderSlot(limiter.TryAcquireRenderSlot());
        slot.Dispose();
        slot.Dispose();
        using var next = limiter.TryAcquireRenderSlot();
        using var blocked = limiter.TryAcquireRenderSlot();

        Assert.True(next.IsAcquired);
        Assert.False(blocked.IsAcquired);
        await Task.CompletedTask;
    }

    [Fact]
    public void Filter_order_places_the_limits_around_request_protection()
    {
        Assert.True(RazorKitFilterOrder.ResponseHeaders < RazorKitFilterOrder.RateLimit);
        Assert.Equal(-8_000, RazorKitFilterOrder.RateLimit);
        Assert.Equal(-6_000, RazorKitFilterOrder.ConcurrencyLimit);
        // Request protection takes -7_000 in the same release, so the backstop must stay after it.
        Assert.True(RazorKitFilterOrder.RateLimit < -7_000 && -7_000 < RazorKitFilterOrder.ConcurrencyLimit);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    private sealed class CapturingLogger : ILogger<RazorKitRateLimiter>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public int Count => _messages.Count;

        public IReadOnlyCollection<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _messages.Enqueue(formatter(state, exception));
    }
}
