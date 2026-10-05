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

    internal static ArchiveWarning Warning(string sourcePath, CoreText text) =>
        new() { SourcePath = sourcePath, Message = text.English, Text = text };

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
    internal static CoreText FromException(Exception exception) => Detail(exception);

    /// <summary>T-F297: corrupt content with a message Core wrote — still an
    /// <see cref="InvalidDataException"/>, so every existing catch of corrupt content applies.</summary>
    internal static InvalidDataException InvalidData(CoreText text) => new(text.English, new CoreTextCarrier(text));

    /// <summary>T-F333: archive data that ends early, with a message Core wrote.</summary>
    internal static EndOfStreamException EndOfStream(CoreText text) => new(text.English, new CoreTextCarrier(text));

    /// <summary>
    /// T-F297: an exception's text as a message detail — its own code when Core threw it; otherwise
    /// the text as Windows or .NET wrote it (English, after <paramref name="rewrite"/>), plus the
    /// Windows error code when there is one. The common Windows errors get a code of their own, so a
    /// frontend can put a translation in front of the English text.
    /// </summary>
    internal static CoreText Detail(Exception exception, Func<string, string>? rewrite = null)
    {
        if ((exception as ICoreTextSource ?? exception.InnerException as CoreTextCarrier) is { } source)
            return source.Text;
        string text = rewrite is null ? exception.Message : rewrite(exception.Message);
        if (WindowsErrorCode(exception) is not { } hResult)
            return CoreText.Raw(text);
        string code = "0x" + hResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        return SystemErrorCodes.TryGetValue(hResult, out MessageCode mapped)
            ? Text(mapped, text, code)
            : CoreText.Raw($"{text} ({code})");
    }

    private static readonly Dictionary<int, MessageCode> SystemErrorCodes = new()
    {
        [unchecked((int)0x8007007B)] = MessageCode.SystemInvalidName,      // ERROR_INVALID_NAME
        [unchecked((int)0x80070005)] = MessageCode.SystemAccessDenied,     // ERROR_ACCESS_DENIED
        [unchecked((int)0x80070020)] = MessageCode.SystemSharingViolation, // ERROR_SHARING_VIOLATION
        [unchecked((int)0x80070021)] = MessageCode.SystemSharingViolation, // ERROR_LOCK_VIOLATION
        [unchecked((int)0x80070070)] = MessageCode.SystemDiskFull,         // ERROR_DISK_FULL
        [unchecked((int)0x80070027)] = MessageCode.SystemDiskFull,         // ERROR_HANDLE_DISK_FULL
    };

    // A rewrap (new IOException(..., inner)) keeps the Windows code on the inner exception.
    private static int? WindowsErrorCode(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (((uint)current.HResult & 0xFFFF0000u) == 0x80070000u)
                return current.HResult;
        }
        return null;
    }

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

/// <summary>
/// Carries Core's text as the inner exception of an exception type Core cannot derive from
/// (<see cref="InvalidDataException"/> is sealed); see <see cref="CoreMessages.InvalidData"/>.
/// </summary>
internal sealed class CoreTextCarrier(CoreText text) : Exception(text.English), ICoreTextSource // NOSONAR: S3871 — internal, never thrown on its own
{
    public CoreText Text { get; } = text;
}
