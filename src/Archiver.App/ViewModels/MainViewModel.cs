using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Archiver.App.Services;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;

namespace Archiver.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IArchiveService _archiveService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    private bool _isBusy = false;

    [ObservableProperty]
    private int _progress = 0;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _destinationPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    [ObservableProperty]
    private ObservableCollection<string> _selectedPaths = [];

    public MainViewModel(IArchiveService archiveService, IDialogService dialogService)
    {
        _archiveService = archiveService;
        _dialogService = dialogService;
        _selectedPaths.CollectionChanged += (_, _) =>
        {
            ArchiveCommand.NotifyCanExecuteChanged();
            ExtractCommand.NotifyCanExecuteChanged();
            UpdateDefaultDestination();
        };
    }

    private void UpdateDefaultDestination()
    {
        if (SelectedPaths.Count > 0)
            DestinationPath = Path.GetDirectoryName(SelectedPaths[0]) ?? DestinationPath;
        else
            DestinationPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    [RelayCommand]
    private async Task BrowseDestinationAsync()
    {
        var folder = await _dialogService.PickDestinationFolderAsync();
        if (folder is not null)
            DestinationPath = folder;
    }

    [RelayCommand(CanExecute = nameof(CanArchive))]
    private async Task ArchiveAsync()
    {
        IsBusy = true;
        Progress = 0;
        StatusMessage = "Archiving...";
        try
        {
            var options = new ArchiveOptions
            {
                SourcePaths = [.. SelectedPaths],
                DestinationFolder = DestinationPath,
            };
            var progress = new Progress<int>(p => Progress = p);
            var result = await _archiveService.ArchiveAsync(options, progress);
            StatusMessage = result.Success
                ? $"Done — {result.CreatedFiles.Count} archive(s) created."
                : $"Completed with {result.Errors.Count} error(s).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExtract))]
    private async Task ExtractAsync()
    {
        IsBusy = true;
        Progress = 0;
        StatusMessage = "Extracting...";
        try
        {
            var options = new ExtractOptions
            {
                ArchivePaths = [.. SelectedPaths],
                DestinationFolder = DestinationPath,
            };
            var progress = new Progress<int>(p => Progress = p);
            var result = await _archiveService.ExtractAsync(options, progress);
            StatusMessage = result.Success
                ? $"Done — {result.CreatedFiles.Count} file(s) extracted."
                : $"Completed with {result.Errors.Count} error(s).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanArchive() => !IsBusy && SelectedPaths.Count > 0;
    private bool CanExtract() => !IsBusy && SelectedPaths.Count > 0;

    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            if (!SelectedPaths.Contains(path))
                SelectedPaths.Add(path);
    }

    [RelayCommand]
    private void Clear() => SelectedPaths.Clear();

    public void RemovePath(string path) => SelectedPaths.Remove(path);

    [RelayCommand]
    private async Task BrowseFilesAsync()
    {
        var paths = await _dialogService.PickFilesAsync();
        AddPaths(paths);
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        var paths = await _dialogService.PickFoldersAsync();
        AddPaths(paths);
    }
}
