namespace Archiver.Core.Models;

/// <summary>What an operation is doing when it sends a <see cref="ProgressReport"/> (T-F307).</summary>
public enum ProgressPhase
{
    /// <summary>Reading or writing data; <see cref="ProgressReport.Percent"/> and the byte counts move.</summary>
    Transferring = 0,

    /// <summary>
    /// Checking the archive before anything is written (tar-family pre-scan: whole-archive
    /// listing passes). The percent does not move; a frontend shows a status instead.
    /// </summary>
    CheckingArchive,
}
