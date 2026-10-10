using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.App.Core;

/// <summary>How the recovery line reads at a glance; the App maps it to an InfoBar severity.</summary>
public enum RecoveryPanelSeverity
{
    /// <summary>PAR2 files were found; nothing is known about them yet.</summary>
    Informational,
    /// <summary>The archive matches its set.</summary>
    Success,
    /// <summary>The set is not a verdict on the archive, or the archive could not be opened.</summary>
    Warning,
    /// <summary>The set says the archive is damaged.</summary>
    Error,
}

/// <summary>
/// T-F275 step 3c: the Archive Browser's line about the PAR2 files next to the open archive.
/// Before a test it says only that files were found (whether they are usable is known after the
/// test); after a test it carries Core's verdict for that archive. Pure, so the rules are tested
/// without a window.
/// </summary>
/// <param name="HasFiles">PAR2 files lie next to the archive (<see cref="RecoveryDataLookup.HasFilesFor"/>).</param>
/// <param name="Severity">Informational until a test has run.</param>
/// <param name="Text">The line shown.</param>
public sealed record RecoveryPanel(bool HasFiles, RecoveryPanelSeverity Severity, string Text)
{
    private static readonly HashSet<MessageCode> Verdicts =
    [
        MessageCode.RecoveryDataDamagedRepairable, MessageCode.RecoveryDataDamagedNotRepairable,
        MessageCode.RecoveryDataRepairTooLarge, MessageCode.RecoveryDataDoesNotMatch, MessageCode.RecoveryDataUnusable,
        MessageCode.RecoveryDataForAnotherFile, MessageCode.RecoveryDataNameMismatch,
    ];

    /// <summary>No PAR2 files, or none looked for: nothing is shown.</summary>
    public static RecoveryPanel None { get; } = new(false, RecoveryPanelSeverity.Informational, string.Empty);

    /// <summary>
    /// Files were found and no test has run. <paramref name="listingError"/> is why the archive
    /// could not be opened, when it could not: the archive stays open for the test instead of
    /// the error being all the user gets (the case recovery data exists for).
    /// </summary>
    public static RecoveryPanel Found(string foundText, string? listingError = null) => listingError is null
        ? new RecoveryPanel(true, RecoveryPanelSeverity.Informational, foundText)
        : new RecoveryPanel(true, RecoveryPanelSeverity.Warning, listingError + " " + foundText);

    /// <summary>
    /// The panel after a test of <paramref name="archivePath"/>: what the set said about that
    /// archive, in Core's words. Another archive's verdict is never taken, and a result that says
    /// nothing about a set (none was found after all) leaves the panel as it was.
    /// </summary>
    public RecoveryPanel After(string archivePath, ArchiveResult result, Func<CoreText?, string, string> render)
    {
        string key = FullPath(archivePath);
        string[] errors = [.. result.Errors
            .Where(e => IsVerdict(e.Text) && FullPath(e.SourcePath) == key).Select(e => render(e.Text, e.Message))];
        string[] warnings = [.. result.Warnings
            .Where(w => IsVerdict(w.Text) && FullPath(w.SourcePath) == key).Select(w => render(w.Text, w.Message))];
        string[] matches = [.. result.RecoveryChecks
            .Where(c => c.Text is not null && FullPath(c.ArchivePath) == key).Select(c => render(c.Text, c.Text!.English))];

        string[] lines = [.. errors, .. warnings, .. matches];
        if (lines.Length == 0)
            return this;
        var severity = RecoveryPanelSeverity.Success;
        if (errors.Length > 0)
            severity = RecoveryPanelSeverity.Error;
        else if (warnings.Length > 0)
            severity = RecoveryPanelSeverity.Warning;
        return new RecoveryPanel(true, severity, string.Join(" ", lines));
    }

    /// <summary>The lines a "no errors" dialog adds for <paramref name="archivePath"/>: that its set matches.</summary>
    public static string? MatchLine(string archivePath, ArchiveResult result, Func<CoreText?, string, string> render)
    {
        string key = FullPath(archivePath);
        RecoveryCheck? check = result.RecoveryChecks.FirstOrDefault(c => c.Text is not null && FullPath(c.ArchivePath) == key);
        return check is null ? null : render(check.Text, check.Text!.English);
    }

    /// <summary>
    /// Whether the panel shows where the user is: only in the archive opened from disk. A nested
    /// archive is a temporary copy with no set next to it, and a real folder is not an archive.
    /// Where it shows, Test is offered even if <see cref="BrowseLocationState.ShowsTest"/> alone
    /// would hide it: a tar-family archive, or one whose listing failed, is checked by its set.
    /// </summary>
    public bool IsOpenAt(bool insideArchive, bool nested) => HasFiles && insideArchive && !nested;

    private static bool IsVerdict(CoreText? text) => text is not null && Verdicts.Contains(text.Code);

    private static string FullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path).ToUpperInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.ToUpperInvariant(); // not a usable path; it only has to compare equal to itself
        }
    }
}
