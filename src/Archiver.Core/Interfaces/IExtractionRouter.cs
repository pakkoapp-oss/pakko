using Archiver.Core.Models;

namespace Archiver.Core.Interfaces;

/// <summary>
/// Routes ExtractAsync calls to IArchiveService (ZIP) or ITarService (tar-family) per archive,
/// based on ArchiveFormatDetector, and merges the results. Never throws — all errors are
/// captured in ArchiveResult.Errors.
/// </summary>
public interface IExtractionRouter
{
    /// <summary>Extracts one or more archives, routed per-archive by format. Cancellation in
    /// either engine throws OperationCanceledException (T-F260); nothing is merged then.</summary>
    Task<ArchiveResult> ExtractAsync(
        ExtractOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies archives without writing anything (T-F261): ZIP archives through
    /// IArchiveService.TestAsync; tar-family archives are skipped, since tar.exe has no test
    /// mode; archives refused by Group Policy or tar.exe's capabilities are skipped with that
    /// reason. Never starts tar.exe. Cancellation throws OperationCanceledException.
    /// <para>With <paramref name="verifyRecoveryData"/> (T-F275 step 3), each archive is also
    /// checked against a PAR2 set found next to it, a <c>.par2</c> path stands for the archive its
    /// set protects, and a tar-family archive with a usable set is checked by it instead of being
    /// skipped; <see cref="ArchiveResult.RecoveryChecks"/> says what each set found. Under
    /// <see cref="GroupPolicyOptions.DisableRecoveryData"/> no set is looked for and a <c>.par2</c>
    /// path is refused.</para>
    /// </summary>
    Task<ArchiveResult> TestAsync(
        IReadOnlyList<string> archivePaths,
        IProgress<ProgressReport>? progress = null,
        Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync = null,
        bool verifyRecoveryData = false,
        CancellationToken cancellationToken = default);
}
