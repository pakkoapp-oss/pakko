using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Archiver.Shell;

/// <summary>
/// Starts Archiver.OperationUi.exe from Shell's own folder (an absolute path, never PATH) over two
/// anonymous pipes. The handles go on the command line as numbers; nothing secret ever does.
/// </summary>
internal sealed class HelperProcessLauncher : IHelperLauncher
{
    public const string ExeName = "Archiver.OperationUi.exe";

    public HelperConnection Launch()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (!File.Exists(exe))
            throw new FileNotFoundException("The operation window helper is missing.", exe);

        var toHelper = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var fromHelper = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            start.ArgumentList.Add("--in");
            start.ArgumentList.Add(toHelper.GetClientHandleAsString());
            start.ArgumentList.Add("--out");
            start.ArgumentList.Add(fromHelper.GetClientHandleAsString());
            process = Process.Start(start) ?? throw new InvalidOperationException("The operation window helper did not start.");
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
        return new HelperConnection(toHelper, fromHelper, () => Kill(process), process);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception)
        {
            // Exiting right now; nothing left to end.
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
