namespace Archiver.Core.Models;

/// <summary>Which direction a password is being requested for — see <see cref="PasswordPromptInfo.Purpose"/>.</summary>
public enum PasswordPurpose
{
    /// <summary>Reading a password-protected archive (T-F189).</summary>
    Decrypt,

    /// <summary>
    /// Creating a password-protected ZIP (T-F193, AES-256 only) — asked once per ArchiveAsync call,
    /// never retried, since there is nothing to check a new password against.
    /// </summary>
    Encrypt
}

/// <summary>
/// Passed to <see cref="ExtractOptions.ResolvePasswordAsync"/>/<see cref="ArchiveOptions.ResolvePasswordAsync"/>
/// once per archive, before the entry loop runs — never once per entry (see docs/TASKS.md's T-F189 entry).
/// </summary>
public sealed record PasswordPromptInfo
{
    public required string ArchiveName { get; init; }
    public required PasswordPurpose Purpose { get; init; }

    /// <summary>1-based; retried attempts increment this so a UI can show "attempt 2 of 3".</summary>
    public int AttemptNumber { get; init; } = 1;

    /// <summary>True from the second attempt onward, when the previous password was rejected.</summary>
    public bool PreviousAttemptWasWrong { get; init; }
}

/// <summary>The caller's answer to one <see cref="PasswordPromptInfo"/> prompt.</summary>
public sealed record PasswordDecision
{
    /// <summary>Null means the user cancelled — treated exactly like today's no-resolver-wired case.</summary>
    public string? Password { get; init; }

    /// <summary>
    /// When true and <see cref="Password"/> is accepted, the internal PasswordResolver remembers it
    /// for the remainder of the current ExtractAsync/ArchiveAsync call — mirrors
    /// <see cref="ConflictDecision.ApplyToAll"/>.
    /// </summary>
    public bool ApplyToRemaining { get; init; }
}
