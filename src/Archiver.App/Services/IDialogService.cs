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
    Task ShowAboutAsync();
}
