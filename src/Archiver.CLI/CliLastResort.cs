namespace Archiver.CLI;

/// <summary>
/// T-F406: the last line of defence around a whole <c>pakko</c> run. Core reports failures in its
/// results (its contract test checks that); what still escapes is a defect, and it ends the run the
/// way 7-Zip's <c>MainAr.cpp</c> ends one — the error on stderr and exit code 2 (kFatalError) —
/// instead of the runtime's unhandled-exception report and a crash code a script cannot read.
/// </summary>
public static class CliLastResort
{
    public const int FatalError = 2;

    public static async Task<int> RunAsync(Func<Task<int>> run, TextWriter stderr)
    {
        try
        {
            return await run().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The type and stack go too: this is a defect, and the report is what gets it fixed.
            await stderr.WriteLineAsync($"pakko: error: Unexpected error: {ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
            await stderr.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return FatalError;
        }
    }
}
