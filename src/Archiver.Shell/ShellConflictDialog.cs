using System.Runtime.InteropServices;
using Archiver.Core.Models;
using Archiver.OperationUi.Protocol;

namespace Archiver.Shell;

// T-F155: interactive Overwrite/Rename/Skip + "apply to all" conflict dialog for Archiver.Shell's
// extraction commands, matching the WinUI App's own ContentDialog (T-F06, DialogService.cs)
// shape. TaskDialogIndirect (comctl32) is the only Win32 primitive with custom button labels --
// MessageBoxW can't produce them. Requires the comctl32 v6 manifest dependency in app.manifest.
//
// TASKDIALOGCONFIG *and* TASKDIALOG_BUTTON are both declared inside commctrl.h's shared
// pshpack1.h/poppack.h block -- both need Pack = 1. Confirmed empirically via a throwaway spike
// (T-F155, DECISIONS.md): a plain Sequential-layout TASKDIALOG_BUTTON, 16 bytes and naturally
// aligned, reliably crashed TaskDialogIndirect with an AccessViolationException. Pack = 1,
// giving 12 tightly-packed bytes, fixed it. Don't revert this to natural alignment.
public static partial class ShellConflictDialog
{
    private const int IdOverwrite = 1001;
    private const int IdRename = 1002;
    private const int IdSkip = 1003;

    /// <summary>
    /// Pure button-ID + checkbox-state → <see cref="ConflictDecision"/> mapping, kept separate
    /// from the P/Invoke body so it's unit-testable without a real dialog. IDCANCEL (2, returned
    /// on Esc/Alt-F4 since TDF_ALLOW_DIALOG_CANCELLATION is set) and anything unrecognized map to
    /// Skip -- the same safe-default convention <see cref="ConflictResolver"/> already documents
    /// for a null callback.
    /// </summary>
    public static ConflictDecision MapResult(int buttonId, bool applyToAllChecked) => buttonId switch
    {
        IdOverwrite => new ConflictDecision { Resolution = ConflictResolution.Overwrite, ApplyToAll = applyToAllChecked },
        IdRename => new ConflictDecision { Resolution = ConflictResolution.Rename, ApplyToAll = applyToAllChecked },
        _ => new ConflictDecision { Resolution = ConflictResolution.Skip, ApplyToAll = applyToAllChecked },
    };

    /// <summary>
    /// T-F253: the full path (same-named files in several folders are otherwise indistinguishable)
    /// and both files' size and date — the same details the operation window shows.
    /// </summary>
    public static string BuildContent(ConflictInfo conflict)
    {
        AskConflict details = OperationWindowText.CreateAskConflict(0, conflict);
        var lines = new List<string> { conflict.ExistingPath, string.Empty };
        if (details.ExistingDetails is { } existing)
            lines.Add($"{OperationTextLocalizer.Get("WindowExistingFile")}: {existing}");
        if (details.IncomingDetails is { } incoming)
        {
            string newer = details.IncomingIsNewer ? $" ({OperationTextLocalizer.Get("WindowNewer")})" : string.Empty;
            lines.Add($"{OperationTextLocalizer.Get("WindowIncomingFile")}: {incoming}{newer}");
        }
        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    public static Task<ConflictDecision> ShowAsync(ConflictInfo conflict)
    {
        try
        {
            return Task.FromResult(ShowCore(conflict));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            // Missing/broken comctl32 v6 activation context -- e.g. a packaged build without the
            // manifest dependency wired correctly -- must degrade, not crash the whole extraction,
            // same reasoning as Win32OperationUi's COMException catch around the progress dialog.
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip });
        }
    }

    private static ConflictDecision ShowCore(ConflictInfo conflict)
    {
        (int ButtonId, string Text)[] buttons =
        [
            (IdOverwrite, ConflictDialogLocalizer.Get("ConflictDialogOverwriteButton")),
            (IdRename, ConflictDialogLocalizer.Get("ConflictDialogRenameButton")),
            (IdSkip, ConflictDialogLocalizer.Get("ConflictDialogSkipButton")),
        ];
        // Enter resolves to Skip, not Overwrite -- mirrors T-F06's DialogService.ShowConflictDialogAsync.
        (int hr, int selectedButtonId, bool verificationChecked) = ShowTaskDialog(
            ConflictDialogLocalizer.Get("ConflictDialogTitle"),
            ConflictDialogLocalizer.Get("ConflictDialogMessage", Path.GetFileName(conflict.ExistingPath)),
            BuildContent(conflict), buttons, IdSkip, ConflictDialogLocalizer.Get("ConflictDialogApplyToAllCheck"));
        if (hr != 0) // S_OK -- a nonzero HRESULT (e.g. E_INVALIDARG) means no real user choice was made
            return new ConflictDecision { Resolution = ConflictResolution.Skip };

        return MapResult(selectedButtonId, verificationChecked);
    }

    /// <summary>
    /// One warning TaskDialog with custom buttons, brought in front of other windows. Shared by
    /// the conflict prompt and <see cref="ShellConfirmDialog"/>.
    /// </summary>
    internal static (int Hr, int SelectedButtonId, bool VerificationChecked) ShowTaskDialog(
        string title, string instruction, string content, (int ButtonId, string Text)[] buttonSpecs, int defaultButtonId,
        string? verificationText)
    {
        TaskDialogButton[] buttons = [.. buttonSpecs.Select(b => new TaskDialogButton { ButtonId = b.ButtonId, ButtonText = b.Text })];
        int buttonStructSize = Marshal.SizeOf<TaskDialogButton>();
        IntPtr buttonsPtr = Marshal.AllocHGlobal(buttonStructSize * buttons.Length);
        IntPtr configPtr = IntPtr.Zero;
        try
        {
            for (int i = 0; i < buttons.Length; i++)
                Marshal.StructureToPtr(buttons[i], buttonsPtr + i * buttonStructSize, false);

            var config = new TaskDialogConfig
            {
                Size = (uint)Marshal.SizeOf<TaskDialogConfig>(),
                Flags = TaskDialogOptions.AllowDialogCancellation | TaskDialogOptions.SizeToContent,
                WindowTitle = title,
                MainIcon = TaskDialogIcon.Warning,
                MainInstruction = instruction,
                Content = content,
                ButtonCount = (uint)buttons.Length,
                Buttons = buttonsPtr,
                DefaultButtonId = defaultButtonId,
                VerificationText = verificationText,
                Callback = Marshal.GetFunctionPointerForDelegate(BringToFrontCallback),
            };

            // T-F287: the config's LPWStr fields keep it non-blittable, so it goes out by pointer
            // the same way the buttons do; [LibraryImport] passes only blittable structs by ref.
            configPtr = Marshal.AllocHGlobal(Marshal.SizeOf<TaskDialogConfig>());
            Marshal.StructureToPtr(config, configPtr, false);
            int hr = NativeMethods.TaskDialogIndirect(configPtr, out int selectedButtonId, out _, out bool verificationChecked);
            return (hr, selectedButtonId, verificationChecked);
        }
        finally
        {
            if (configPtr != IntPtr.Zero)
            {
                Marshal.DestroyStructure<TaskDialogConfig>(configPtr);
                Marshal.FreeHGlobal(configPtr);
            }
            for (int i = 0; i < buttons.Length; i++)
                Marshal.DestroyStructure<TaskDialogButton>(buttonsPtr + i * buttonStructSize);
            Marshal.FreeHGlobal(buttonsPtr);
        }
    }

    // T-F253: started from a background process (Explorer's verb), the dialog opened behind the
    // foreground window and the extraction waited on it invisibly. Same fix as PasswordDialog
    // (T-F192): topmost Z-order, which a window's own process may always set.
    private static readonly NativeMethods.TaskDialogCallback BringToFrontCallback = (hwnd, notification, _, _, _) =>
    {
        if (notification == NativeMethods.TDN_CREATED)
        {
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        return 0;
    };

    [Flags]
    private enum TaskDialogOptions : uint
    {
        AllowDialogCancellation = 0x0008,
        SizeToContent = 0x01000000,
    }

    // MAKEINTRESOURCEW(-1): (WORD)(-1) zero-extended to a pointer value, NOT (IntPtr)(-1)
    // (which would sign-extend to all bits set on a 64-bit pointer -- a completely different,
    // invalid value). Confirmed via the same T-F155 spike.
    private static class TaskDialogIcon
    {
        public static readonly IntPtr Warning = (IntPtr)0xFFFF;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    private struct TaskDialogConfig
    {
        public uint Size;
        public IntPtr OwnerWindowHandle;
        public IntPtr InstanceHandle;
        public TaskDialogOptions Flags;
        public uint CommonButtons;
        [MarshalAs(UnmanagedType.LPWStr)] public string? WindowTitle;
        public IntPtr MainIcon;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MainInstruction;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Content;
        public uint ButtonCount;
        public IntPtr Buttons;
        public int DefaultButtonId;
        public uint RadioButtonCount;
        public IntPtr RadioButtons;
        public int DefaultRadioButtonId;
        [MarshalAs(UnmanagedType.LPWStr)] public string? VerificationText;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ExpandedInformation;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ExpandedControlText;
        [MarshalAs(UnmanagedType.LPWStr)] public string? CollapsedControlText;
        public IntPtr FooterIcon;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Footer;
        public IntPtr Callback;
        public IntPtr CallbackData;
        public uint Width;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    private struct TaskDialogButton
    {
        public int ButtonId;
        [MarshalAs(UnmanagedType.LPWStr)] public string ButtonText;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("comctl32.dll")]
        public static partial int TaskDialogIndirect(
            IntPtr config, out int selectedButtonId, out int selectedRadioButtonId,
            [MarshalAs(UnmanagedType.Bool)] out bool verificationFlagChecked);

        public const uint TDN_CREATED = 0;
        public static readonly IntPtr HWND_TOPMOST = new(-1);
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public delegate int TaskDialogCallback(IntPtr hwnd, uint notification, IntPtr wParam, IntPtr lParam, IntPtr refData);

        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
        [LibraryImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] public static partial bool SetForegroundWindow(IntPtr hWnd);
    }
}
