namespace Archiver.Core.Models;

/// <summary>
/// T-F146. Named ThreatVerdict/ThreatFinding/ThreatScanResult (not "Scan*") deliberately —
/// TarSandboxedService.ScanForUnsafeEntriesAsync already owns "Scan" for the unrelated T-F49
/// traversal/symlink pre-scan; keeping the two grep-separable avoids confusing them.
/// Inconclusive must never be silently rendered as Clean by any caller — it means "Pakko could not
/// determine an answer" (no AMSI provider registered, a provider call failed, an entry was too
/// large to buffer, or a tar-family entry vanished/became unreadable between extraction and scan),
/// which is a different fact from a scan that genuinely came back negative.
/// </summary>
public enum ThreatVerdict
{
    Clean,
    ThreatDetected,
    Inconclusive,
}

/// <summary>One entry-level (or whole-archive-level, when EntryPath is null) scan outcome.</summary>
public sealed record ThreatFinding
{
    public required string ArchivePath { get; init; }

    /// <summary>Null when the finding applies to the whole archive (e.g. an unsupported format, or
    /// "no AMSI provider registered") rather than one specific entry inside it.</summary>
    public string? EntryPath { get; init; }

    public required ThreatVerdict Verdict { get; init; }

    /// <summary>Set only when Verdict is ThreatDetected and the provider returned a name.</summary>
    public string? ThreatName { get; init; }

    /// <summary>Set for Inconclusive (why Pakko couldn't determine an answer) and for
    /// ThreatDetected when no specific ThreatName was available. Never set for Clean.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Kept out of ArchiveResult/ArchiveError on purpose (see docs/DECISIONS.md's T-F146 entry) — a
/// scan is not an extraction outcome and needs its own three-state result, not a two-state
/// success/failure one.
/// </summary>
public sealed record ThreatScanResult
{
    public required ThreatVerdict OverallVerdict { get; init; }
    public IReadOnlyList<ThreatFinding> Findings { get; init; } = [];
}
