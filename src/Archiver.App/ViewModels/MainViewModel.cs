using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Archiver.App.Models;
using Archiver.App.Services;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.Resources;

namespace Archiver.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly ResourceLoader _res = ResourceLoader.GetForViewIndependentUse();

    private readonly IArchiveService _archiveService;
    private readonly IDialogService _dialogService;
    private readonly ILogService _logService;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(IsOperationRunning))]
    [NotifyPropertyChangedFor(nameof(IsOperationRunningVisibility))]
    private bool _isBusy = false;

    [ObservableProperty]
    private bool _isIndeterminate = false;

    [ObservableProperty]
    private int _progress = 0;

    public bool IsOperationRunning => IsBusy;

    public Visibility IsOperationRunningVisibility =>
        IsBusy ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    private string _statusMessage = _res.GetString("StatusReady");

    [ObservableProperty]
    private string _destinationPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    [ObservableProperty]
    private ObservableCollection<FileItem> _fileItems = [];

    [ObservableProperty]
    private string? _archiveName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSingleArchive))]
    [NotifyPropertyChangedFor(nameof(IsSeparateArchives))]
    [NotifyPropertyChangedFor(nameof(IsArchiveNameEnabled))]
    private ArchiveMode _selectedArchiveMode = ArchiveMode.SingleArchive;

    public bool IsSingleArchive
    {
        get => SelectedArchiveMode == ArchiveMode.SingleArchive;
        set { if (value) SelectedArchiveMode = ArchiveMode.SingleArchive; }
    }

    public bool IsSeparateArchives
    {
        get => SelectedArchiveMode == ArchiveMode.SeparateArchives;
        set { if (value) SelectedArchiveMode = ArchiveMode.SeparateArchives; }
    }

    public bool IsArchiveNameEnabled => SelectedArchiveMode == ArchiveMode.SingleArchive;

    public bool IsFileListEmpty => FileItems.Count == 0;

    public Visibility IsFileListEmptyVisibility =>
        FileItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnConflictIndex))]
    private ConflictBehavior _onConflict = ConflictBehavior.Skip;

    public int OnConflictIndex
    {
        get => OnConflict switch
        {
            ConflictBehavior.Overwrite => 0,
            ConflictBehavior.Rename    => 2,
            _                          => 1 // Skip
        };
        set => OnConflict = value switch
        {
            0 => ConflictBehavior.Overwrite,
            2 => ConflictBehavior.Rename,
            _ => ConflictBehavior.Skip
        };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompressionLevelIndex))]
    private CompressionLevel _selectedCompressionLevel = CompressionLevel.Fastest;

    public int CompressionLevelIndex
    {
        get => SelectedCompressionLevel switch
        {
            CompressionLevel.Fastest       => 0,
            CompressionLevel.Optimal       => 1,
            CompressionLevel.SmallestSize  => 2,
            CompressionLevel.NoCompression => 3,
            _                              => 0
        };
        set => SelectedCompressionLevel = value switch
        {
            0 => CompressionLevel.Fastest,
            1 => CompressionLevel.Optimal,
            2 => CompressionLevel.SmallestSize,
            3 => CompressionLevel.NoCompression,
            _ => CompressionLevel.Fastest
        };
    }

    [ObservableProperty]
    private bool _openDestinationFolder = false;

    [ObservableProperty]
    private bool _deleteAfterOperation = false;

    private string _sortColumn = "Name";
    private bool _sortAscending = true;

    public MainViewModel(IArchiveService archiveService, IDialogService dialogService, ILogService logService)
    {
        _archiveService = archiveService;
        _dialogService = dialogService;
        _logService = logService;
        _fileItems.CollectionChanged += (_, _) =>
        {
            ArchiveCommand.NotifyCanExecuteChanged();
            ExtractCommand.NotifyCanExecuteChanged();
            UpdateDefaultDestination();
            OnPropertyChanged(nameof(IsFileListEmpty));
            OnPropertyChanged(nameof(IsFileListEmptyVisibility));
        };
    }

    private void UpdateDefaultDestination()
    {
        if (FileItems.Count > 0)
            DestinationPath = Path.GetDirectoryName(FileItems[0].FullPath) ?? DestinationPath;
        else
            DestinationPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    [RelayCommand]
    private void SortBy(string column)
    {
        if (_sortColumn == column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }
        ApplySort();
    }

    private void ApplySort()
    {
        var sorted = (_sortColumn switch
        {
            "Type"     => _sortAscending ? FileItems.OrderBy(x => x.Type)     : FileItems.OrderByDescending(x => x.Type),
            "Size"     => _sortAscending ? FileItems.OrderBy(x => x.SizeBytes) : FileItems.OrderByDescending(x => x.SizeBytes),
            "Modified" => _sortAscending ? FileItems.OrderBy(x => x.Modified) : FileItems.OrderByDescending(x => x.Modified),
            _          => _sortAscending ? FileItems.OrderBy(x => x.Name)     : FileItems.OrderByDescending(x => x.Name),
        }).ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            int current = FileItems.IndexOf(sorted[i]);
            if (current != i)
                FileItems.Move(current, i);
        }
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
        _cts = new CancellationTokenSource();
        IsBusy = true;
        CancelCommand.NotifyCanExecuteChanged();
        Progress = 0;
        IsIndeterminate = false;
        StatusMessage = _res.GetString("StatusArchiving");
        bool wasCancelled = false;
        try
        {
            var options = new ArchiveOptions
            {
                SourcePaths = [.. FileItems.Select(x => x.FullPath)],
                DestinationFolder = DestinationPath,
                ArchiveName = string.IsNullOrWhiteSpace(ArchiveName) ? null : ArchiveName.Trim(),
                Mode = SelectedArchiveMode,
                OnConflict = OnConflict,
                OpenDestinationFolder = OpenDestinationFolder,
                DeleteSourceFiles = DeleteAfterOperation,
                CompressionLevel = SelectedCompressionLevel,
            };
            var progress = new Progress<int>(p =>
            {
                if (p < 0) { IsIndeterminate = true; Progress = 0; }
                else { IsIndeterminate = false; Progress = p; }
            });
            var result = await _archiveService.ArchiveAsync(options, progress, _cts.Token);
            StatusMessage = result.Errors.Count == 0 && result.SkippedFiles.Count == 0
                ? _res.GetString("StatusDone").Replace("{0}", result.CreatedFiles.Count.ToString())
                : _res.GetString("StatusIssues");
            _logService.Info($"Archive completed — {result.CreatedFiles.Count} file(s) → {DestinationPath}");
            foreach (var skipped in result.SkippedFiles)
                _logService.Warn($"Skipped {skipped.Path} — {skipped.Reason}");
            foreach (var error in result.Errors)
                _logService.Error($"{error.SourcePath} — {error.Message}");
            await _dialogService.ShowOperationSummaryAsync("Archive", result);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            StatusMessage = _res.GetString("StatusCancelled");
        }
        catch (Exception ex)
        {
            StatusMessage = "Error";
            _logService.Error("Unexpected error during operation", ex);
            await _dialogService.ShowErrorAsync("Error", ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
        if (wasCancelled)
        {
            await Task.Delay(2000);
        }
        StatusMessage = _res.GetString("StatusReady");
    }

    [RelayCommand(CanExecute = nameof(CanExtract))]
    private async Task ExtractAsync()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        CancelCommand.NotifyCanExecuteChanged();
        Progress = 0;
        IsIndeterminate = false;
        StatusMessage = _res.GetString("StatusExtracting");
        bool wasCancelled = false;
        try
        {
            var options = new ExtractOptions
            {
                ArchivePaths = [.. FileItems.Select(x => x.FullPath)],
                DestinationFolder = DestinationPath,
                OnConflict = OnConflict,
                OpenDestinationFolder = OpenDestinationFolder,
                DeleteArchiveAfterExtraction = DeleteAfterOperation,
            };
            var progress = new Progress<int>(p =>
            {
                if (p < 0) { IsIndeterminate = true; Progress = 0; }
                else { IsIndeterminate = false; Progress = p; }
            });
            var result = await _archiveService.ExtractAsync(options, progress, _cts.Token);
            StatusMessage = result.Errors.Count == 0 && result.SkippedFiles.Count == 0
                ? _res.GetString("StatusDone").Replace("{0}", result.CreatedFiles.Count.ToString())
                : _res.GetString("StatusIssues");
            _logService.Info($"Extract completed — {result.CreatedFiles.Count} file(s) → {DestinationPath}");
            foreach (var skipped in result.SkippedFiles)
                _logService.Warn($"Skipped {skipped.Path} — {skipped.Reason}");
            foreach (var error in result.Errors)
                _logService.Error($"{error.SourcePath} — {error.Message}");
            await _dialogService.ShowOperationSummaryAsync("Extract", result);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            StatusMessage = _res.GetString("StatusCancelled");
        }
        catch (Exception ex)
        {
            StatusMessage = "Error";
            _logService.Error("Unexpected error during operation", ex);
            await _dialogService.ShowErrorAsync("Error", ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
        if (wasCancelled)
        {
            await Task.Delay(2000);
        }
        StatusMessage = _res.GetString("StatusReady");
    }

    [RelayCommand(CanExecute = nameof(IsOperationRunning))]
    private void Cancel() => _cts?.Cancel();

    private bool CanArchive() => !IsBusy && FileItems.Count > 0;
    private bool CanExtract() => !IsBusy && FileItems.Count > 0;
    private bool CanOperate() => !IsBusy;

    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            if (!FileItems.Any(x => x.FullPath == path))
                FileItems.Add(new FileItem(path));
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private void Clear() => FileItems.Clear();

    public void RemovePath(string path)
    {
        var item = FileItems.FirstOrDefault(x => x.FullPath == path);
        if (item is not null)
            FileItems.Remove(item);
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task BrowseFilesAsync()
    {
        var paths = await _dialogService.PickFilesAsync();
        AddPaths(paths);
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task BrowseFolderAsync()
    {
        var paths = await _dialogService.PickFoldersAsync();
        AddPaths(paths);
    }
}
