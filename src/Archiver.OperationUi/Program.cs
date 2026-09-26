using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Archiver.OperationUi;

/// <summary>
/// Archiver.OperationUi.exe --in &lt;handle&gt; --out &lt;handle&gt;: the two anonymous pipe handles
/// Archiver.Shell passes (T-F268). Nothing else is ever on the command line.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!TryParse(args, out string inHandle, out string outHandle))
            return 2;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new HelperApp(inHandle, outHandle);
        });
        return 0;
    }

    private static bool TryParse(string[] args, out string inHandle, out string outHandle)
    {
        inHandle = "";
        outHandle = "";
        if (args.Length != 4 || args[0] != "--in" || args[2] != "--out")
            return false;
        if (!long.TryParse(args[1], out _) || !long.TryParse(args[3], out _))
            return false;
        inHandle = args[1];
        outHandle = args[3];
        return true;
    }
}
