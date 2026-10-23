namespace Manifesto.RazorKit.Filters;

/// <summary>
/// A read-only wrapper that throws once more than a set number of bytes has been read, so a body
/// without a Content-Length is still capped on servers that do not enforce a body size limit.
/// </summary>
internal sealed class LengthLimitedStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private long _read;

    public LengthLimitedStream(Stream inner, long limit)
    {
        _inner = inner;
        _limit = limit;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Count(_inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await _inner.ReadAsync(buffer, cancellationToken));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Count(int read)
    {
        _read += read;
        if (_read > _limit)
        {
            throw new LimitExceededException();
        }

        return read;
    }

    internal sealed class LimitExceededException : IOException
    {
        public LimitExceededException()
            : base("The request body is larger than RazorKit accepts.")
        {
        }
    }
}
