using System.Text;
using System.Text.Json.Serialization;

namespace Archiver.OperationUi.Protocol;

/// <summary>
/// One message between Archiver.Shell and the operation window helper (T-F268 step 3). The helper
/// never touches files: Shell does the work and sends every text the window shows, already localized.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Begin), "begin")]
[JsonDerivedType(typeof(Item), "item")]
[JsonDerivedType(typeof(Progress), "progress")]
[JsonDerivedType(typeof(AskConflict), "askConflict")]
[JsonDerivedType(typeof(AskPassword), "askPassword")]
[JsonDerivedType(typeof(Complete), "complete")]
[JsonDerivedType(typeof(HelperReady), "ready")]
[JsonDerivedType(typeof(CancelRequested), "cancel")]
[JsonDerivedType(typeof(ConflictAnswer), "conflictAnswer")]
[JsonDerivedType(typeof(PasswordAnswer), "passwordAnswer")]
[JsonDerivedType(typeof(WindowClosed), "closed")]
public abstract record ProtocolMessage;

/// <summary>How the progress line is read: bytes and speed, or a percentage only (the scan).</summary>
public enum ProgressKind
{
    Bytes,
    Percent,
}

/// <summary>Picks the result's icon.</summary>
public enum ResultSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>The user's answer to a file conflict.</summary>
public enum ConflictChoice
{
    Skip,
    Overwrite,
    Rename,
}

// --- Shell -> helper ---

/// <summary>First message. <paramref name="Strings"/> holds every label the window draws.</summary>
public sealed record Hello(int ProtocolVersion, string Culture, bool RightToLeft, IReadOnlyDictionary<string, string> Strings) : ProtocolMessage;

/// <summary>Starts the one window of an Explorer command.</summary>
public sealed record Begin(string Title, ProgressKind Kind) : ProtocolMessage;

/// <summary>The archive now being processed, <paramref name="Index"/> of <paramref name="Count"/> (1-based).</summary>
public sealed record Item(string Name, int Index, int Count) : ProtocolMessage;

/// <summary><paramref name="Status"/> is formatted by Shell (bytes, speed, time left).</summary>
public sealed record Progress(int Percent, string? CurrentFile, string? Status) : ProtocolMessage;

/// <summary>Sizes and dates are null when unknown.</summary>
public sealed record AskConflict(
    int RequestId,
    string ExistingPath,
    long? ExistingSize,
    DateTimeOffset? ExistingModified,
    long? IncomingSize,
    DateTimeOffset? IncomingModified) : ProtocolMessage;

public sealed record AskPassword(
    int RequestId,
    string ArchiveName,
    int AttemptNumber,
    bool PreviousAttemptWasWrong,
    bool CanApplyToRemaining) : ProtocolMessage;

/// <summary>A result to show, or null to close the window without one (a clean Extract/Archive).</summary>
public sealed record ResultText(ResultSeverity Severity, string Title, string Text);

public sealed record Complete(ResultText? Result) : ProtocolMessage;

// --- helper -> Shell ---

/// <summary>The window exists (hidden until it has something to show).</summary>
public sealed record HelperReady(int ProtocolVersion) : ProtocolMessage;

/// <summary>Cancel or the window's close button while the operation runs.</summary>
public sealed record CancelRequested : ProtocolMessage;

public sealed record ConflictAnswer(int RequestId, ConflictChoice Choice, bool ApplyToAll) : ProtocolMessage;

/// <summary>A null <paramref name="Password"/> means the user declined.</summary>
public sealed record PasswordAnswer(int RequestId, string? Password, bool ApplyToRemaining) : ProtocolMessage
{
    // A record's generated ToString prints every member; a password must never reach a log that way.
    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"RequestId = {RequestId}, Password = {(Password is null ? "null" : "***")}, ApplyToRemaining = {ApplyToRemaining}");
        return true;
    }
}

/// <summary>The window closed. Sent before the helper exits; EOF without it means a crash.</summary>
public sealed record WindowClosed : ProtocolMessage;
