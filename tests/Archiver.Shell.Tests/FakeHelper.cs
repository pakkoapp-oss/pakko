using System.IO.Pipes;
using Archiver.OperationUi.Protocol;
using Archiver.Shell;

namespace Archiver.Shell.Tests;

// T-F268 step 4: plays the operation window helper over two real in-process anonymous pipes, so
// HelperOperationUi is tested on the same stream type and EOF behavior as the real helper process.
internal sealed class FakeHelper : IHelperLauncher, IDisposable
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private AnonymousPipeServerStream? _toHelperServer;
    private AnonymousPipeServerStream? _fromHelperServer;
    private AnonymousPipeClientStream? _in;
    private AnonymousPipeClientStream? _out;
    private int _killed;

    /// <summary>Simulates a missing or unstartable Archiver.OperationUi.exe.</summary>
    public bool FailToLaunch { get; set; }

    public int Launches { get; private set; }

    public bool Killed => Volatile.Read(ref _killed) == 1;

    public HelperConnection Launch()
    {
        if (FailToLaunch)
            throw new FileNotFoundException("The operation window helper is missing.", "Archiver.OperationUi.exe");

        Launches++;
        _toHelperServer = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        _in = new AnonymousPipeClientStream(PipeDirection.In, _toHelperServer.ClientSafePipeHandle);
        _fromHelperServer = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.None);
        _out = new AnonymousPipeClientStream(PipeDirection.Out, _fromHelperServer.ClientSafePipeHandle);
        return new HelperConnection(_toHelperServer, _fromHelperServer, Kill);
    }

    /// <summary>The next message Shell sent, or null at EOF.</summary>
    /// <remarks>On its own thread: ReadAsync on this synchronous pipe would hold a pool thread (T-F456).</remarks>
    public Task<ProtocolMessage?> ReadAsync()
    {
        AnonymousPipeClientStream input = _in!;
        return Task.Factory.StartNew(() => FrameCodec.Read(input), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(ReadTimeout);
    }

    /// <summary>Skips progress until a message of type <typeparamref name="T"/>.</summary>
    public async Task<T> ReadUntilAsync<T>() where T : ProtocolMessage
    {
        while (true)
        {
            ProtocolMessage? message;
            try
            {
                message = await ReadAsync();
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException($"Shell sent no message within {ReadTimeout} while the fake waited for {typeof(T).Name}.", ex);
            }
            if (message is T wanted)
                return wanted;
            if (message is null)
                throw new EndOfStreamException($"Shell closed the pipe before sending {typeof(T).Name}.");
        }
    }

    public Task SendAsync(ProtocolMessage message)
    {
        // No flush: an anonymous pipe has no write buffer, and Shell may end this fake (Kill) the
        // moment it reads the frame, which would fail a flush after a write that already arrived.
        // Written here, not through WriteAsync, which would wait on a pool thread (T-F456).
        _out!.Write(FrameCodec.Encode(message));
        return Task.CompletedTask;
    }

    public Task SendReadyAsync() => SendAsync(new HelperReady(FrameCodec.ProtocolVersion));

    /// <summary>Ends the helper without WindowClosed: Shell reads EOF.</summary>
    public void Crash() => _out?.Dispose();

    public void Dispose()
    {
        _out?.Dispose();
        _in?.Dispose();
    }

    private void Kill()
    {
        Interlocked.Exchange(ref _killed, 1);
        Dispose();
    }
}
