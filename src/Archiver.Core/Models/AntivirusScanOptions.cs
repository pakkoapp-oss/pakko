namespace Archiver.Core.Models;

/// <summary>
/// T-F146. Deliberately not ExtractOptions — a scan never has a destination/conflict/MOTW
/// dimension, so reusing that record would carry a pile of meaningless properties.
/// </summary>
public sealed record AntivirusScanOptions
{
    public required IReadOnlyList<string> ArchivePaths { get; init; }

    /// <summary>Same convention as ExtractOptions.SelectedEntryPaths (T-F97/T-F98) — null/empty
    /// means "scan the whole archive"; only meaningful when ArchivePaths has exactly one entry.</summary>
    public IReadOnlyList<string>? SelectedEntryPaths { get; init; }

    /// <summary>T-F194: same hook as <see cref="ExtractOptions.ResolvePasswordAsync"/> — asked once
    /// per archive that contains an encrypted ZIP entry, so that entry's decrypted plaintext can be
    /// handed to AMSI. Null (or a declined/wrong password) leaves every encrypted entry
    /// Inconclusive ("password-protected, not scanned") — never Clean.</summary>
    public Func<PasswordPromptInfo, Task<PasswordDecision>>? ResolvePasswordAsync { get; init; }
}
