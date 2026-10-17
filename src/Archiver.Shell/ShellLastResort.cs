namespace Archiver.Shell;

/// <summary>
/// T-F406: the last line of defence around an Explorer command. Core reports failures in its
/// results; what still escapes is a defect, and Explorer's user saw nothing at all — this WinExe
/// has no console, and the process just ended. Now it says the operation failed, with the
/// exception's type and text for a bug report, and exits 2. The message goes through the native
/// dialog, not the operation window: the window's helper may be what failed.
/// </summary>
internal static class ShellLastResort
{
    internal const int FatalError = 2;

    internal static async Task<int> RunAsync(Func<Task> run, IOperationUi fallbackUi)
    {
        try
        {
            await run().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            fallbackUi.ShowMessage(new OperationMessage(
                "Pakko", MessageSeverity.Error,
                $"{ResultMessagesLocalizer.Get("ResultOperationFailed")}{Environment.NewLine}{Environment.NewLine}{ex.GetType().Name}: {ex.Message}"));
            return FatalError;
        }
    }
}
