using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Archiver.Shell;

/// <summary>
/// Starts Archiver.OperationUi.exe from Shell's own folder (an absolute path, never PATH) over two
/// anonymous pipes. The handles go on the command line as numbers; nothing secret ever does.
/// The helper inherits those two handles and no others (<see cref="HandleListProcess"/>, T-F356).
/// </summary>
internal sealed partial class HelperProcessLauncher : IHelperLauncher
{
    public const string ExeName = "Archiver.OperationUi.exe";

    public HelperConnection Launch()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (!File.Exists(exe))
            throw new FileNotFoundException("The operation window helper is missing.", exe);

        var toHelper = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var fromHelper = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        HandleListProcess? process = null;
        try
        {
            process = HandleListProcess.Start(
                exe,
                ["--in", toHelper.GetClientHandleAsString(), "--out", fromHelper.GetClientHandleAsString()],
                [toHelper.ClientSafePipeHandle, fromHelper.ClientSafePipeHandle]);
        }
        finally
        {
            // The helper holds its own copies now. Shell must not: a later child could inherit them,
            // and Shell's copy of the write end would keep EOF from ever arriving.
            toHelper.DisposeLocalCopyOfClientHandle();
            fromHelper.DisposeLocalCopyOfClientHandle();
            if (process is null)
            {
                toHelper.Dispose();
                fromHelper.Dispose();
            }
        }

        // T-F253: Shell was started by the user's click, so it may hand the foreground on.
        _ = AllowSetForegroundWindow(process.Id);
        return new HelperConnection(toHelper, fromHelper, process.Kill, process);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
