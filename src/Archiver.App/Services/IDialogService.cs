using Archiver.Core.Models;

namespace Archiver.App.Services;

public interface IDialogService
{
    Task ShowErrorAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message);
    Task<string?> PickDestinationFolderAsync();
    Task<IReadOnlyList<string>> PickFilesAsync();
    Task<IReadOnlyList<string>> PickFoldersAsync();
    Task ShowOperationSummaryAsync(string operationName, ArchiveResult result);
    Task ShowThreatScanResultAsync(ThreatScanResult result);
    Task ShowAboutAsync();
    Task ShowFileHashAsync();
    Task<bool> ShowCompressionBombConfirmAsync(CompressionBombWarning warning);
    Task<ConflictDecision> ShowConflictDialogAsync(ConflictInfo conflict);
    Task<bool> OpenFileWithDefaultAppAsync(string filePath);

    // T-F190: canApplyToRemaining is a caller/frontend decision (batch shape), not something
    // Archiver.Core's PasswordPromptInfo carries — see docs/DECISIONS.md's T-F190 entry.
    Task<PasswordDecision> ShowPasswordPromptAsync(PasswordPromptInfo info, bool canApplyToRemaining);

    // T-F207: owner for the shell's own delete UI, and the two "Delete after operation" dialogs.
    IntPtr OwnerWindowHandle { get; }
    Task<bool> ShowPermanentDeleteConfirmAsync(IReadOnlyList<string> paths);
    Task ShowNotDeletedAsync(IReadOnlyList<string> paths);
}
