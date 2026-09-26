using System.IO.Pipes;
using Archiver.OperationUi.Protocol;

namespace Archiver.OperationUi;

/// <summary>The helper's two anonymous pipe ends to Archiver.Shell.</summary>
internal sealed class ShellPipe : IDisposable
{
    private readonly AnonymousPipeClientStream _in;
    private readonly AnonymousPipeClientStream _out;
    private readonly MessageWriter _writer;

    public ShellPipe(string inHandle, string outHandle)
    {
        _in = new AnonymousPipeClientStream(PipeDirection.In, inHandle);
        _out = new AnonymousPipeClientStream(PipeDirection.Out, outHandle);
        _writer = new MessageWriter(_out);
    }

    /// <summary>
    /// Blocks until the frame is written, so a WindowClosed is in the pipe before the process exits:
    /// EOF without it reads as a crash to Shell. A Shell that is already gone is not an error here.
    /// </summary>
    public void Send(ProtocolMessage message)
    {
        try
        {
            _writer.WriteAsync(message, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (IOException)
        {
            // Shell has exited; nobody is left to tell.
        }
        catch (ObjectDisposedException)
        {
            // The helper is shutting down.
        }
    }

    /// <summary>Calls <paramref name="received"/> per message, then once with null when Shell's end closes.</summary>
    public async Task ReadAllAsync(Action<ProtocolMessage?> received)
    {
        try
        {
            while (await FrameCodec.ReadAsync(_in, CancellationToken.None).ConfigureAwait(false) is { } message)
                received(message);
        }
        catch (Exception ex) when (ex is ProtocolException or IOException or ObjectDisposedException)
        {
            // A damaged stream ends the conversation the same way EOF does.
        }
        received(null);
    }

    public void Dispose()
    {
        _writer.Dispose();
        _out.Dispose();
        _in.Dispose();
    }
}
