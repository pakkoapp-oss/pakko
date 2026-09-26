using System.Runtime.InteropServices;
using Archiver.Core.Models;

namespace Archiver.Shell;

// T-F192: native password prompt for Archiver.Shell's extraction commands, at parity with the
// WinUI App's own ContentDialog (T-F190) and Archiver.CLI's masked prompt (T-F191). Design
// decision (see DECISIONS.md): a custom in-memory DIALOGEX-shaped template via
// DialogBoxIndirectParamW, NOT CredUIPromptForCredentialsW — confirmed by fetching NanaZip's real
// PasswordDialog.rc/.cpp, which use exactly this shape (EDITTEXT with ES_PASSWORD|
// ES_AUTOHSCROLL, a "Show password" checkbox toggling EM_SETPASSWORDCHAR) and never CredUI.
public static class PasswordDialog
{
    private const int IdOk = 1;     // IDOK
    private const int IdCancel = 2; // IDCANCEL

    /// <summary>
    /// Pure button-ID + field mapping, kept separate from the P/Invoke body so it's unit-testable
    /// without a real dialog — mirrors <see cref="ShellConflictDialog.MapResult"/>. Cancel (or
    /// anything unrecognized) maps to a null password, the same safe-default convention
    /// <see cref="Services.PasswordResolver"/> already documents for a null/declining callback.
    /// </summary>
    public static PasswordDecision MapResult(int buttonId, string editText, bool applyToRemainingChecked) =>
        buttonId == IdOk
            ? new PasswordDecision { Password = editText, ApplyToRemaining = applyToRemainingChecked }
            : new PasswordDecision { Password = null };

    public static Task<PasswordDecision> ShowAsync(PasswordPromptInfo info, bool canApplyToRemaining)
    {
        try
        {
            return Task.FromResult(ShowCore(info, canApplyToRemaining));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            // Same degradation the App/CLI's own layers fall back to when no resolver is
            // effectively wired: null password -> the unchanged "password-protected and cannot be
            // extracted" rejection at the ZipArchiveService call site. A dialog-construction
            // failure must never crash or hang the whole extraction.
            return Task.FromResult(new PasswordDecision { Password = null });
        }
    }

    private static PasswordDecision ShowCore(PasswordPromptInfo info, bool canApplyToRemaining)
    {
        string message = info.PreviousAttemptWasWrong
            ? PasswordDialogLocalizer.Get("PasswordDialogWrongPasswordHint") + " " +
              PasswordDialogLocalizer.Get("PasswordDialogMessage", info.ArchiveName)
            : PasswordDialogLocalizer.Get("PasswordDialogMessage", info.ArchiveName);

        byte[] template = PasswordDialogTemplateBuilder.Build(
            title: PasswordDialogLocalizer.Get("PasswordDialogTitle"),
            message: message,
            canApplyToRemaining: canApplyToRemaining,
            applyToRemainingLabel: PasswordDialogLocalizer.Get("PasswordDialogApplyToRemainingCheck"),
            showPasswordLabel: PasswordDialogLocalizer.Get("PasswordDialogShowPasswordCheck"),
            okLabel: PasswordDialogLocalizer.Get("PasswordDialogOkButton"),
            cancelLabel: PasswordDialogLocalizer.Get("PasswordDialogCancelButton"));

        var state = new DialogState();

        NativeMethods.DialogProcDelegate proc = (hwndDlg, msg, wParam, lParam) => msg switch
        {
            NativeMethods.WM_INITDIALOG => OnInitDialog(hwndDlg),
            NativeMethods.WM_COMMAND => OnCommand(hwndDlg, wParam, canApplyToRemaining, state),
            _ => IntPtr.Zero,
        };

        IntPtr templatePtr = Marshal.AllocHGlobal(template.Length);
        try
        {
            Marshal.Copy(template, 0, templatePtr, template.Length);
            IntPtr hInstance = NativeMethods.GetModuleHandle(null);
            NativeMethods.DialogBoxIndirectParam(hInstance, templatePtr, IntPtr.Zero, proc, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(templatePtr);
            GC.KeepAlive(proc);
        }

        return MapResult(state.ButtonResult, state.EditText, state.ApplyChecked);
    }

    // What the dialog procedure records for ShowCore to read back once DialogBoxIndirectParam returns.
    private sealed class DialogState
    {
        public string EditText { get; set; } = string.Empty;
        public bool ApplyChecked { get; set; }
        public int ButtonResult { get; set; } = IdCancel;
    }

    private static IntPtr OnInitDialog(IntPtr hwndDlg)
    {
        // Plain SetForegroundWindow is NOT reliable from this call site — the caller
        // runs on a background thread while Archiver.Shell's own IProgressDialog is
        // already showing (see Win32OperationUi's session), and Windows' foreground-
        // lock heuristic silently blocks a background process from stealing focus.
        // Confirmed empirically in a Phase 0 spike (DECISIONS.md): without the
        // SetWindowPos(HWND_TOPMOST) below, the dialog was created successfully
        // (IsWindowVisible true) but stayed behind every other window, unreachable,
        // until it timed out. HWND_TOPMOST only changes Z-order, which a window's own
        // owning process can always do — no foreground-donation permission needed.
        NativeMethods.SetWindowPos(hwndDlg, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
        NativeMethods.SetForegroundWindow(hwndDlg);
        NativeMethods.SetActiveWindow(hwndDlg);
        NativeMethods.BringWindowToTop(hwndDlg);
        NativeMethods.FlashWindow(hwndDlg, true);
        NativeMethods.SetFocus(NativeMethods.GetDlgItem(hwndDlg, PasswordDialogTemplateBuilder.IdEdit));
        return IntPtr.Zero; // we set focus ourselves -> return FALSE
    }

    private static IntPtr OnCommand(IntPtr hwndDlg, IntPtr wParam, bool canApplyToRemaining, DialogState state)
    {
        int controlId = (int)(wParam.ToInt64() & 0xFFFF);
        int notifyCode = (int)((wParam.ToInt64() >> 16) & 0xFFFF);

        if (controlId == PasswordDialogTemplateBuilder.IdShowPassword && notifyCode == NativeMethods.BN_CLICKED)
        {
            IntPtr editHwnd = NativeMethods.GetDlgItem(hwndDlg, PasswordDialogTemplateBuilder.IdEdit);
            bool nowChecked = NativeMethods.IsDlgButtonChecked(hwndDlg, PasswordDialogTemplateBuilder.IdShowPassword) != 0;
            NativeMethods.SendMessage(editHwnd, NativeMethods.EM_SETPASSWORDCHAR, nowChecked ? IntPtr.Zero : (IntPtr)'*', IntPtr.Zero);
            NativeMethods.InvalidateRect(editHwnd, IntPtr.Zero, true);
            return (IntPtr)1;
        }

        if (controlId != IdOk && controlId != IdCancel)
            return IntPtr.Zero;

        if (controlId == IdOk)
        {
            var buffer = new char[256];
            int length = NativeMethods.GetDlgItemText(hwndDlg, PasswordDialogTemplateBuilder.IdEdit, buffer, buffer.Length);
            state.EditText = new string(buffer, 0, length);
            if (canApplyToRemaining)
                state.ApplyChecked = NativeMethods.IsDlgButtonChecked(hwndDlg, PasswordDialogTemplateBuilder.IdApplyToRemaining) != 0;
        }
        state.ButtonResult = controlId;
        NativeMethods.EndDialog(hwndDlg, controlId);
        return (IntPtr)1;
    }

    private static class NativeMethods
    {
        public const int WM_INITDIALOG = 0x0110;
        public const int WM_COMMAND = 0x0111;
        public const int BN_CLICKED = 0;
        public const int EM_SETPASSWORDCHAR = 0x00CC;
        public static readonly IntPtr HWND_TOPMOST = new(-1);
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public delegate IntPtr DialogProcDelegate(IntPtr hwndDlg, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DialogBoxIndirectParam(IntPtr hInstance, IntPtr lpTemplate, IntPtr hWndParent, DialogProcDelegate lpDialogFunc, IntPtr dwInitParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetDlgItemText(IntPtr hDlg, int nIDDlgItem, char[] lpString, int nMaxCount);
        [DllImport("user32.dll")] public static extern int IsDlgButtonChecked(IntPtr hDlg, int nIDButton);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EndDialog(IntPtr hDlg, int nResult);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr SetActiveWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
        [DllImport("user32.dll")] public static extern bool FlashWindow(IntPtr hWnd, bool bInvert);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
