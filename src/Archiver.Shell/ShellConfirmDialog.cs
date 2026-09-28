using System.Runtime.InteropServices;

namespace Archiver.Shell;

/// <summary>
/// The Win32 fallback for a yes/no question (T-F217) when the operation window is not available.
/// Declining is the default button, and anything but the confirm button is a no.
/// </summary>
internal static class ShellConfirmDialog
{
    private const int IdConfirm = 1101;
    private const int IdDecline = 1102;

    /// <summary>Only an explicit click on the confirm button, with the dialog shown, is a yes.</summary>
    public static bool MapResult(int hr, int buttonId) => hr == 0 && buttonId == IdConfirm;

    public static Task<bool> ShowAsync(ConfirmPrompt prompt)
    {
        try
        {
            (int hr, int buttonId, _) = ShellConflictDialog.ShowTaskDialog(
                prompt.Title, prompt.Title, prompt.Message,
                [(IdConfirm, prompt.ConfirmLabel), (IdDecline, prompt.DeclineLabel)], IdDecline, verificationText: null);
            return Task.FromResult(MapResult(hr, buttonId));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            // Same degradation as the conflict dialog: no dialog means the question stays unanswered, a no.
            return Task.FromResult(false);
        }
    }
}
