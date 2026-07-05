using System.Runtime.InteropServices;

namespace Archiver.Shell;

// Windows Shell's built-in progress UI (shell32's CLSID_ProgressDialog). Runs entirely
// in-process via COM, on its own internal worker thread — no separate .exe, no IPC, and no
// WindowsAppRuntime/WinUI3 activation to go wrong. See DECISIONS.md T-F65 for why the previous
// Archiver.ProgressWindow.exe + named-pipe design was replaced with this.
[ComImport]
[Guid("F8383852-FCD3-11D1-A6B9-006097DF5BD4")]
internal class ProgressDialogCoClass;

[ComImport]
[Guid("EBBC7C04-315E-11D2-B62F-006097DF5BD4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IProgressDialog
{
    void StartProgressDialog(IntPtr hwndParent, [MarshalAs(UnmanagedType.IUnknown)] object? punkEnableModless, uint dwFlags, IntPtr pvReserved);
    void StopProgressDialog();
    void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pwzTitle);
    void SetAnimation(IntPtr hInstAnimation, ushort idAnimation);
    // Unlike every other method on this interface, HasUserCancelled returns a plain BOOL,
    // not HRESULT — [PreserveSig] is required or the interop marshaller misreads the return
    // value as an HRESULT and treats the bool as a hidden [out] param, so this always reads
    // back false (the observed bug: Cancel appeared to do nothing).
    [PreserveSig]
    [return: MarshalAs(UnmanagedType.Bool)]
    bool HasUserCancelled();
    void SetProgress(uint dwCompleted, uint dwTotal);
    void SetProgress64(ulong ullCompleted, ulong ullTotal);
    void SetLine(uint dwLineNum, [MarshalAs(UnmanagedType.LPWStr)] string pwzString, [MarshalAs(UnmanagedType.Bool)] bool fCompactPath, IntPtr pvReserved);
    void SetCancelMsg([MarshalAs(UnmanagedType.LPWStr)] string pwzCancelMsg, IntPtr pvReserved);
    void Timer(uint dwTimerAction, IntPtr pvReserved);
}

[Flags]
internal enum ProgressDialogFlags : uint
{
    Normal = 0x00000000,
    AutoTime = 0x00000002,
    NoMinimize = 0x00000008,
}

internal sealed class NativeProgressDialog : IDisposable
{
    private readonly IProgressDialog _dialog;

    public NativeProgressDialog(string title)
    {
        _dialog = (IProgressDialog)new ProgressDialogCoClass();
        _dialog.SetTitle(title);
        _dialog.StartProgressDialog(IntPtr.Zero, null,
            (uint)(ProgressDialogFlags.Normal | ProgressDialogFlags.AutoTime | ProgressDialogFlags.NoMinimize),
            IntPtr.Zero);
    }

    public bool HasUserCancelled() => _dialog.HasUserCancelled();

    public void SetLine(uint lineNum, string text) => _dialog.SetLine(lineNum, text, false, IntPtr.Zero);

    public void SetProgress(long completed, long total) =>
        _dialog.SetProgress64((ulong)completed, (ulong)Math.Max(total, 1));

    public void Dispose() => _dialog.StopProgressDialog();
}
