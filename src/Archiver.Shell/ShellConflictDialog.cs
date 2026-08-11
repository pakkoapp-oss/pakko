using System.Runtime.InteropServices;
using Archiver.Core.Models;

namespace Archiver.Shell;

// T-F155: interactive Overwrite/Rename/Skip + "apply to all" conflict dialog for Archiver.Shell's
// extraction commands, matching the WinUI App's own ContentDialog (T-F06, DialogService.cs)
// shape. TaskDialogIndirect (comctl32) is the only Win32 primitive with custom button labels --
// MessageBoxW can't produce them. Requires the comctl32 v6 manifest dependency in app.manifest.
//
// TASKDIALOGCONFIG *and* TASKDIALOG_BUTTON are both declared inside commctrl.h's shared
// pshpack1.h/poppack.h block -- both need Pack = 1. Confirmed empirically via a throwaway spike
// (T-F155, DECISIONS.md): a plain [StructLayout(LayoutKind.Sequential)] TASKDIALOG_BUTTON (16
// bytes, naturally aligned) reliably crashed TaskDialogIndirect with AccessViolationException;
// Pack = 1 (12 bytes, tightly packed) fixed it. Don't "simplify" this back to natural alignment.
public static class ShellConflictDialog
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

    public static Task<ConflictDecision> ShowAsync(ConflictInfo conflict)
    {
        try
        {
            return Task.FromResult(ShowCore(conflict));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            // Missing/broken comctl32 v6 activation context (e.g. a packaged build without the
            // manifest dependency wired correctly) must degrade, not crash the whole extraction --
            // same reasoning as TryCreateProgressDialog's existing catch(COMException) in Program.cs.
            return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Skip });
        }
    }

    private static ConflictDecision ShowCore(ConflictInfo conflict)
    {
        var buttons = new TaskDialogButton[]
        {
            new() { ButtonId = IdOverwrite, ButtonText = ConflictDialogLocalizer.Get("ConflictDialogOverwriteButton") },
            new() { ButtonId = IdRename, ButtonText = ConflictDialogLocalizer.Get("ConflictDialogRenameButton") },
            new() { ButtonId = IdSkip, ButtonText = ConflictDialogLocalizer.Get("ConflictDialogSkipButton") },
        };

        int buttonStructSize = Marshal.SizeOf<TaskDialogButton>();
        IntPtr buttonsPtr = Marshal.AllocHGlobal(buttonStructSize * buttons.Length);
        try
        {
            for (int i = 0; i < buttons.Length; i++)
                Marshal.StructureToPtr(buttons[i], buttonsPtr + i * buttonStructSize, false);

            var config = new TaskDialogConfig
            {
                Size = (uint)Marshal.SizeOf<TaskDialogConfig>(),
                Flags = TaskDialogFlags.AllowDialogCancellation | TaskDialogFlags.SizeToContent,
                WindowTitle = ConflictDialogLocalizer.Get("ConflictDialogTitle"),
                MainIcon = TaskDialogIcon.Warning,
                MainInstruction = ConflictDialogLocalizer.Get("ConflictDialogMessage", Path.GetFileName(conflict.ExistingPath)),
                Content = string.Empty,
                ButtonCount = (uint)buttons.Length,
                Buttons = buttonsPtr,
                DefaultButtonId = IdSkip, // Enter resolves to Skip, not Overwrite -- mirrors T-F06's
                                          // DialogService.ShowConflictDialogAsync's identical choice.
                VerificationText = ConflictDialogLocalizer.Get("ConflictDialogApplyToAllCheck"),
            };

            int hr = NativeMethods.TaskDialogIndirect(ref config, out int selectedButtonId, out _, out bool verificationChecked);
            if (hr != 0) // S_OK -- a nonzero HRESULT (e.g. E_INVALIDARG) means no real user choice was made
                return new ConflictDecision { Resolution = ConflictResolution.Skip };

            return MapResult(selectedButtonId, verificationChecked);
        }
        finally
        {
            for (int i = 0; i < buttons.Length; i++)
                Marshal.DestroyStructure<TaskDialogButton>(buttonsPtr + i * buttonStructSize);
            Marshal.FreeHGlobal(buttonsPtr);
        }
    }

    [Flags]
    private enum TaskDialogFlags : uint
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
        public TaskDialogFlags Flags;
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

    private static class NativeMethods
    {
        [DllImport("comctl32.dll", CharSet = CharSet.Unicode)]
        public static extern int TaskDialogIndirect(
            ref TaskDialogConfig config, out int selectedButtonId, out int selectedRadioButtonId,
            [MarshalAs(UnmanagedType.Bool)] out bool verificationFlagChecked);
    }
}
