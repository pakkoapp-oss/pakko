namespace Archiver.Core.Models;

/// <summary>
/// What a whole operation achieved (T-F260) — the one classification every frontend maps to its
/// exit code, dialog or status line. Cancellation is not an outcome: it throws
/// <see cref="OperationCanceledException"/>.
/// </summary>
public enum OperationOutcome
{
    /// <summary>Everything requested was done: no errors, nothing skipped.</summary>
    Completed,

    /// <summary>Something was done and something was skipped; no errors.</summary>
    CompletedWithSkips,

    /// <summary>Nothing was done — every source was skipped (already there, unsupported, blocked
    /// by policy, untestable); no errors. A Test with this outcome tested nothing (T-F274).</summary>
    NothingDone,

    /// <summary>At least one error. Other sources may still have completed — see
    /// <see cref="ArchiveResult.Sources"/>.</summary>
    Failed,
}
