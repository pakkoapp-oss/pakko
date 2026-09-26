namespace Archiver.OperationUi.Protocol;

/// <summary>
/// Writes whole frames to one stream. Progress, prompt answers and cancel can be sent from
/// different threads; the lock keeps their frames from interleaving.
/// </summary>
public sealed class MessageWriter(Stream stream) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task WriteAsync(ProtocolMessage message, CancellationToken cancellationToken)
    {
        byte[] frame = FrameCodec.Encode(message);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();
}
