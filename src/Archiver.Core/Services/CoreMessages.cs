using Archiver.Core.Models;

namespace Archiver.Core.Services;

/// <summary>
/// The only place Core builds a user-visible message (T-F209): each factory sets the English field
/// and the <see cref="CoreText"/> from the same code, so they cannot disagree.
/// <c>CoreMessageSourceGuardTests</c> fails on a message field set anywhere else.
/// </summary>
internal static class CoreMessages
{
    internal static CoreText Text(MessageCode code, params object[] arguments) => new(code, arguments);

    internal static ArchiveError Error(string sourcePath, CoreText text, Exception? exception = null) =>
        new() { SourcePath = sourcePath, Message = text.English, Text = text, Exception = exception };

    internal static ArchiveError Error(string sourcePath, MessageCode code, params object[] arguments) =>
        Error(sourcePath, Text(code, arguments));

    internal static SkippedFile Skip(string path, CoreText text) =>
        new() { Path = path, Reason = text.English, Text = text };

    internal static SkippedFile Skip(string path, MessageCode code, params object[] arguments) =>
        Skip(path, Text(code, arguments));

    internal static ArchiveListResult ListFailure(CoreText text) =>
        new() { Success = false, ErrorMessage = text.English, ErrorText = text };

    internal static HashEntry HashError(string sourcePath, CoreText text) =>
        new(sourcePath, null, text.English) { ErrorText = text };

    internal static ThreatFinding Inconclusive(string archivePath, string? entryPath, CoreText text) =>
        new() { ArchivePath = archivePath, EntryPath = entryPath, Verdict = ThreatVerdict.Inconclusive, Reason = text.English, ReasonText = text };

    /// <summary>The text an exception carries: its own code when Core threw it, otherwise its
    /// message as uncoded text.</summary>
    internal static CoreText FromException(Exception exception) =>
        exception is ICoreTextSource source ? source.Text : CoreText.Raw(exception.Message);

    /// <summary>A "Cannot X: {0}" message whose argument is <paramref name="inner"/>'s text.</summary>
    internal static CoreText Wrap(MessageCode code, Exception inner) => Text(code, FromException(inner));
}

/// <summary>An exception whose message Core wrote and can render in another language (T-F209).</summary>
internal interface ICoreTextSource
{
    CoreText Text { get; }
}

/// <summary>
/// An <see cref="IOException"/> Core throws with a user-visible message; caught and turned into an
/// <see cref="ArchiveError"/> like any other I/O failure.
/// </summary>
internal sealed class CoreTextIOException(CoreText text, Exception? innerException = null) // NOSONAR: S3871 — internal, always caught and turned into an ArchiveError (same as SandboxSetupException)
    : IOException(text.English, innerException), ICoreTextSource
{
    public CoreText Text { get; } = text;
}
