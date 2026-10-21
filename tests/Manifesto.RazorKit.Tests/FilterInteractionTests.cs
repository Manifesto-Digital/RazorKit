using System.Net;
using System.Text;
using Manifesto.RazorKit.Tests.Components.Atoms.Waiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using static Manifesto.RazorKit.Tests.Support.RazorKitRequests;

namespace Manifesto.RazorKit.Tests;

/// <summary>
/// How the rate limit, request protection and the render backstop behave together: which
/// rejections spend a client's render token, and which never hold a render slot.
/// </summary>
[Collection(WaitingGateCollection.Name)]
public class FilterInteractionTests
{
    private const string SlowBodyHeader = "X-Test-Slow-Body";
    private const string WaitingPreview = "/razorkit-preview/preview/waiting/default";

    private static Action<Models.RazorKitOptions> Limits(int renderBurst = 1000, int pageBurst = 1000, int maxConcurrent = 100) =>
        o =>
        {
            // One permit a minute, so a bucket never refills during a test.
            o.RateLimit.Render.PermitsPerMinute = 1;
            o.RateLimit.Render.Burst = renderBurst;
            o.RateLimit.Page.PermitsPerMinute = 1;
            o.RateLimit.Page.Burst = pageBurst;
            o.RateLimit.MaxConcurrentRenders = maxConcurrent;
            o.MaxPropsJsonBytes = 1024;
        };

    private static HttpRequestMessage ValidPost(string path = PreviewPath) =>
        WithValidProtection(new HttpRequestMessage(HttpMethod.Post, path));

    private static HttpRequestMessage CrossSitePost()
    {
        var request = WithValidProtection(new HttpRequestMessage(HttpMethod.Post, PreviewPath));
        request.Headers.Remove("Sec-Fetch-Site");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");
        return request;
    }

    private static HttpRequestMessage OversizedPost()
    {
        var request = ValidPost();
        var token = MintToken();
        request.Headers.Remove("Cookie");
        request.Headers.Add("Cookie", $"{CookieName}={token}");
        request.Content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
        {
            new(TokenField, token),
            new("propsJson", new string('a', 64 * 1024))
        });
        return request;
    }

    private static HttpRequestMessage JsonPost()
    {
        var request = ValidPost();
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return request;
    }

    [Fact]
    public async Task A_slow_body_is_read_and_rejected_by_request_protection_without_holding_the_render_slot()
    {
        var bodyReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigureRazorKit = Limits(maxConcurrent: 1),
            // The first read of the body happens inside request protection, which is the only
            // thing that reads it before model binding; this marks the moment it starts.
            ConfigurePipeline = app => app.Use(async (context, next) =>
            {
                if (context.Request.Headers.ContainsKey(SlowBodyHeader))
                {
                    context.Request.Body = new ReadSignallingStream(context.Request.Body, bodyReadStarted);
                }

                await next();
            })
        });

        var token = MintToken();
        var slowBody = new StalledContent($"{TokenField}={token}&propsJson=", new string('a', 64 * 1024));
        var slowRequest = new HttpRequestMessage(HttpMethod.Post, PreviewPath) { Content = slowBody };
        slowRequest.Headers.Add("Cookie", $"{CookieName}={token}");
        slowRequest.Headers.Add("Sec-Fetch-Site", "same-origin");
        slowRequest.Headers.Add(SlowBodyHeader, "1");

        var slow = host.Client.SendAsync(slowRequest);
        try
        {
            await bodyReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // The slow request is stalled mid-body inside request protection. If it held the only
            // render slot, this would be a 503.
            var concurrent = await host.Client.SendAsync(ValidPost());
            Assert.Equal(HttpStatusCode.OK, concurrent.StatusCode);
        }
        finally
        {
            slowBody.Resume();
        }

        var rejected = await slow;
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);

        // And it gave nothing back late, because it never took anything.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(ValidPost())).StatusCode);
    }

    public static TheoryData<string> Rejections => new() { "403", "413", "415" };

    private static (HttpRequestMessage Request, HttpStatusCode Expected) Rejection(string kind) => kind switch
    {
        "403" => (CrossSitePost(), HttpStatusCode.Forbidden),
        "413" => (OversizedPost(), HttpStatusCode.RequestEntityTooLarge),
        "415" => (JsonPost(), HttpStatusCode.UnsupportedMediaType),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public async Task A_rejected_render_request_is_answered_even_when_every_render_slot_is_busy(string kind)
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions { ConfigureRazorKit = Limits(maxConcurrent: 1) });
        WaitingGate.Arm();
        try
        {
            var held = host.Client.SendAsync(ValidPost(WaitingPreview));
            await WaitingGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            // Request protection answers before the backstop is reached, so the busy slot is
            // irrelevant: the client sees its own fault, never 503.
            var (request, expected) = Rejection(kind);
            var response = await host.Client.SendAsync(request);
            Assert.Equal(expected, response.StatusCode);

            WaitingGate.Release();
            Assert.Equal(HttpStatusCode.OK, (await held).StatusCode);
        }
        finally
        {
            WaitingGate.Release();
        }

        // The rejection did not leave a slot taken.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(ValidPost())).StatusCode);
    }

    [Fact]
    public async Task Each_rejected_render_request_spends_exactly_one_render_token()
    {
        // The rate limit runs before request protection, so a rejected render still costs the
        // client one render token, and no more: three rejections plus one render use a burst of four.
        await using var host = await TestHost.StartAsync(new TestHostOptions { ConfigureRazorKit = Limits(renderBurst: 4) });

        foreach (var kind in new[] { "403", "413", "415" })
        {
            var (request, expected) = Rejection(kind);
            Assert.Equal(expected, (await host.Client.SendAsync(request)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(ValidPost())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.Client.SendAsync(ValidPost())).StatusCode);
    }

    [Fact]
    public async Task A_page_405_spends_no_page_token()
    {
        // The method check runs before the rate limit.
        await using var host = await TestHost.StartAsync(new TestHostOptions { ConfigureRazorKit = Limits(pageBurst: 1) });

        for (var i = 0; i < 3; i++)
        {
            var response = await host.Client.SendAsync(ValidPost(ShellPath));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(ShellPath)).StatusCode);
    }

    [Fact]
    public async Task With_method_override_a_cross_site_post_is_rejected_and_an_allowed_one_is_charged_as_a_render()
    {
        await using var host = await TestHost.StartAsync(new TestHostOptions
        {
            ConfigureRazorKit = Limits(renderBurst: 2, pageBurst: 1),
            ConfigurePipeline = app => app.UseHttpMethodOverride()
        });

        HttpRequestMessage Overridden(HttpRequestMessage request)
        {
            request.Headers.Add("X-HTTP-Method-Override", "GET");
            return request;
        }

        var crossSite = Overridden(CrossSitePost());
        crossSite.Content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
        {
            new(TokenField, MintToken()),
            new("propsJson", "{\"Label\":\"Overridden\"}")
        });
        var rejected = await host.Client.SendAsync(crossSite);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal("fetch-site", RejectCode(rejected));
        Assert.DoesNotContain("Overridden", await rejected.Content.ReadAsStringAsync());

        var token = MintToken();
        var allowed = Overridden(new HttpRequestMessage(HttpMethod.Post, PreviewPath)
        {
            Content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
            {
                new(TokenField, token),
                new("propsJson", "{\"Label\":\"Overridden\"}")
            })
        });
        allowed.Headers.Add("Cookie", $"{CookieName}={token}");
        allowed.Headers.Add("Sec-Fetch-Site", "same-origin");
        var rendered = await host.Client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.OK, rendered.StatusCode);
        Assert.Contains("Overridden", await rendered.Content.ReadAsStringAsync());

        // Both overridden POSTs were charged to the render bucket (burst 2), none to the page
        // bucket (burst 1), which still has its token for the shell.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.Client.SendAsync(Overridden(ValidPost()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(ShellPath)).StatusCode);
    }

    /// <summary>Sends <c>head</c>, then waits for <see cref="Resume"/> before sending <c>tail</c>, with no Content-Length.</summary>
    private sealed class StalledContent : HttpContent
    {
        private readonly byte[] _head;
        private readonly byte[] _tail;
        private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StalledContent(string head, string tail)
        {
            _head = Encoding.UTF8.GetBytes(head);
            _tail = Encoding.UTF8.GetBytes(tail);
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");
        }

        public void Resume() => _resume.TrySetResult();

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(_head);
            await stream.FlushAsync();
            await _resume.Task;
            try
            {
                await stream.WriteAsync(_tail);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The server may answer 413 and stop reading before the whole tail is sent.
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    private sealed class ReadSignallingStream : Stream
    {
        private readonly Stream _inner;
        private readonly TaskCompletionSource _firstRead;

        public ReadSignallingStream(Stream inner, TaskCompletionSource firstRead)
        {
            _inner = inner;
            _firstRead = firstRead;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _firstRead.TrySetResult();
            return _inner.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _firstRead.TrySetResult();
            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
