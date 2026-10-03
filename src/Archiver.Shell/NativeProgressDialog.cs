using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Archiver.Shell;

// Windows Shell's built-in progress UI (shell32's CLSID_ProgressDialog). Runs entirely
// in-process via COM, on its own internal worker thread — no separate .exe, no IPC, and no
// WindowsAppRuntime/WinUI3 activation to go wrong. See DECISIONS.md T-F65 for why the previous
// Archiver.ProgressWindow.exe + named-pipe design was replaced with this. T-F288: a
// source-generated interface created through ShellCom; vtable order as in shlobj_core.h.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("EBBC7C04-315E-11D2-B62F-006097DF5BD4")]
internal partial interface IProgressDialog
{
    void StartProgressDialog(IntPtr hwndParent, IntPtr punkEnableModless, uint dwFlags, IntPtr pvReserved);
    void StopProgressDialog();
    void SetTitle(string pwzTitle);
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
    void SetLine(uint dwLineNum, string pwzString, [MarshalAs(UnmanagedType.Bool)] bool fCompactPath, IntPtr pvReserved);
    void SetCancelMsg(string pwzCancelMsg, IntPtr pvReserved);
    void Timer(uint dwTimerAction, IntPtr pvReserved);
}

[Flags]
internal enum ProgressDialogOptions : uint
{
    None = 0x00000000,
    AutoTime = 0x00000002,
    NoMinimize = 0x00000008,
}

internal sealed class NativeProgressDialog : IDisposable
{
    /// <summary>CLSID_ProgressDialog (shell32).</summary>
    public static readonly Guid ClassId = new("F8383852-FCD3-11D1-A6B9-006097DF5BD4");

    private readonly IProgressDialog _dialog;

    public NativeProgressDialog(string title)
    {
        _dialog = ShellCom.Create<IProgressDialog>(ClassId);
        try
        {
            _dialog.SetTitle(title);
            _dialog.StartProgressDialog(IntPtr.Zero, IntPtr.Zero,
                (uint)(ProgressDialogOptions.None | ProgressDialogOptions.AutoTime | ProgressDialogOptions.NoMinimize),
                IntPtr.Zero);
        }
        catch
        {
            ShellCom.Release(_dialog);
            throw;
        }
    }

    public bool HasUserCancelled() => _dialog.HasUserCancelled();

    public void SetTitle(string title) => _dialog.SetTitle(title);

    public void SetLine(uint lineNum, string text) => _dialog.SetLine(lineNum, text, false, IntPtr.Zero);

    public void SetProgress(long completed, long total) =>
        _dialog.SetProgress64((ulong)completed, (ulong)Math.Max(total, 1));

    public void Dispose()
    {
        try
        {
            _dialog.StopProgressDialog();
        }
        finally
        {
            ShellCom.Release(_dialog);
        }
    }
}
