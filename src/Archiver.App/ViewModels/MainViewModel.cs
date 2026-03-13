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

    private System.Diagnostics.Stopwatch? _operationStopwatch;
    private long _lastBytesTransferred;
    private DateTime _lastSpeedSampleTime;
    private double _smoothedBytesPerSec;
    private const double SpeedAlpha = 0.25;
    private string _operationStatusPrefix = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(IsOperationRunning))]
    [NotifyPropertyChangedFor(nameof(IsOperationRunningVisibility))]
    [NotifyPropertyChangedFor(nameof(ArchiveButtonText))]
    [NotifyPropertyChangedFor(nameof(ExtractButtonText))]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(IsArchiveNameAndNotBusy))]
    private bool _isBusy = false;

    private string _lastOperation = string.Empty;

    // Set by protocol activation (pakko://extract or pakko://archive) before the
    // user has pressed either button. Empty when the app was not opened via URI.
    [ObservableProperty]
    private string _requestedOperation = string.Empty;

    [ObservableProperty]
    private int _progress = 0;

    [ObservableProperty]
    private bool _isProgressIndeterminate = false;

    public bool IsOperationRunning => IsBusy;
    public bool IsNotBusy => !IsBusy;

    public string ArchiveButtonText => IsBusy && _lastOperation == "archive" ? "Archiving..." : "Archive";
    public string ExtractButtonText => IsBusy && _lastOperation == "extract" ? "Extracting..." : "Extract";

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
    [NotifyPropertyChangedFor(nameof(IsArchiveNameAndNotBusy))]
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
    public bool IsArchiveNameAndNotBusy => IsArchiveNameEnabled && !IsBusy;

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
        _lastOperation = "archive";
        IsBusy = true;
        CancelCommand.NotifyCanExecuteChanged();
        Progress = 0;
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

            long totalBytes = 0;
            int fileCount = 0;
            foreach (var p in options.SourcePaths)
            {
                try
                {
                    if (File.Exists(p))
                    {
                        totalBytes += new FileInfo(p).Length;
                        fileCount++;
                    }
                    else if (Directory.Exists(p))
                    {
                        foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                        {
                            try { totalBytes += new FileInfo(f).Length; fileCount++; } catch { }
                        }
                    }
                }
                catch { }
            }

            string sizeStr = totalBytes switch
            {
                >= 1_073_741_824 => $"{totalBytes / 1_073_741_824.0:F1} GB",
                >= 1_048_576     => $"{totalBytes / 1_048_576.0:F1} MB",
                >= 1_024         => $"{totalBytes / 1_024.0:F0} KB",
                _                => $"{totalBytes} B"
            };

            _operationStatusPrefix = $"Archiving... ({fileCount} files, {sizeStr})";
            StatusMessage = _operationStatusPrefix;
            _operationStopwatch = System.Diagnostics.Stopwatch.StartNew();
            _lastBytesTransferred = 0;
            _lastSpeedSampleTime = DateTime.UtcNow;
            _smoothedBytesPerSec = 0;

            var progress = new Progress<ProgressReport>(r =>
            {
                Progress = r.Percent;
                if (r.Percent >= 100)
                {
                    IsProgressIndeterminate = true;
                    StatusMessage = _res.GetString("StatusFinalizing");
                }
                else
                {
                    IsProgressIndeterminate = false;
                    UpdateOperationStatus(r);
                }
            });

            var result = await _archiveService.ArchiveAsync(options, progress, _cts.Token);
            if (result.Success && DeleteAfterOperation)
                await RunCleanupAsync(options.SourcePaths);
            _operationStopwatch?.Stop();
            int totalSec = (int)(_operationStopwatch?.Elapsed.TotalSeconds ?? 0);
            if (result.Errors.Count == 0 && result.SkippedFiles.Count == 0)
            {
                StatusMessage = totalSec > 0
                    ? _res.GetString("StatusArchivedIn")
                        .Replace("{0}", totalSec.ToString())
                        .Replace("{1}", result.CreatedFiles.Count.ToString())
                    : _res.GetString("StatusDone")
                        .Replace("{0}", result.CreatedFiles.Count.ToString());
            }
            else
            {
                StatusMessage = _res.GetString("StatusIssues");
            }
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
            _operationStopwatch?.Stop();
            StatusMessage = _res.GetString("StatusCancelled");
        }
        catch (Exception ex)
        {
            _operationStopwatch?.Stop();
            StatusMessage = "Error";
            _logService.Error("Unexpected error during operation", ex);
            await _dialogService.ShowErrorAsync("Error", ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsProgressIndeterminate = false;
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
        _lastOperation = "extract";
        IsBusy = true;
        CancelCommand.NotifyCanExecuteChanged();
        Progress = 0;
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

            _operationStatusPrefix = $"Extracting... ({options.ArchivePaths.Count} archive(s))";
            StatusMessage = _operationStatusPrefix;
            _operationStopwatch = System.Diagnostics.Stopwatch.StartNew();
            _lastBytesTransferred = 0;
            _lastSpeedSampleTime = DateTime.UtcNow;
            _smoothedBytesPerSec = 0;

            var progress = new Progress<ProgressReport>(r =>
            {
                Progress = r.Percent;
                UpdateOperationStatus(r);
            });

            var result = await _archiveService.ExtractAsync(options, progress, _cts.Token);
            if (result.Success && DeleteAfterOperation)
                await RunCleanupAsync(options.ArchivePaths);
            _operationStopwatch?.Stop();
            int totalSec = (int)(_operationStopwatch?.Elapsed.TotalSeconds ?? 0);
            if (result.Errors.Count == 0 && result.SkippedFiles.Count == 0)
            {
                StatusMessage = totalSec > 0
                    ? _res.GetString("StatusExtractedIn")
                        .Replace("{0}", totalSec.ToString())
                        .Replace("{1}", result.CreatedFiles.Count.ToString())
                    : _res.GetString("StatusDone")
                        .Replace("{0}", result.CreatedFiles.Count.ToString());
            }
            else
            {
                StatusMessage = _res.GetString("StatusIssues");
            }
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
            _operationStopwatch?.Stop();
            StatusMessage = _res.GetString("StatusCancelled");
        }
        catch (Exception ex)
        {
            _operationStopwatch?.Stop();
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

    private async Task RunCleanupAsync(IEnumerable<string> paths)
    {
        StatusMessage = _res.GetString("StatusCleaningUp");
        await Task.Run(() =>
        {
            foreach (var path in paths)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else if (File.Exists(path)) File.Delete(path);
                }
                catch { }
            }
        }).ConfigureAwait(false);
    }

    private void UpdateOperationStatus(ProgressReport report)
    {
        if (_operationStopwatch is null) return;

        var now = DateTime.UtcNow;
        var elapsed = _operationStopwatch.Elapsed;

        string speedPart = string.Empty;
        string etaPart = string.Empty;

        if (report.TotalBytes > 0)
        {
            long bytesDelta = report.BytesTransferred - _lastBytesTransferred;
            double timeDelta = (now - _lastSpeedSampleTime).TotalSeconds;

            if (timeDelta >= 0.25 && bytesDelta > 0)
            {
                double instantSpeed = bytesDelta / timeDelta;
                _smoothedBytesPerSec = _smoothedBytesPerSec < 1
                    ? instantSpeed
                    : SpeedAlpha * instantSpeed + (1 - SpeedAlpha) * _smoothedBytesPerSec;
                _lastBytesTransferred = report.BytesTransferred;
                _lastSpeedSampleTime = now;
            }

            if (_smoothedBytesPerSec >= 1)
            {
                speedPart = _smoothedBytesPerSec switch
                {
                    >= 1_073_741_824 => $"{_smoothedBytesPerSec / 1_073_741_824:F1} GB/s",
                    >= 1_048_576     => $"{_smoothedBytesPerSec / 1_048_576:F1} MB/s",
                    >= 1_024         => $"{_smoothedBytesPerSec / 1_024:F0} KB/s",
                    _                => $"{_smoothedBytesPerSec:F0} B/s"
                };
            }

            if (elapsed.TotalSeconds >= 1.0 && report.Percent > 0)
            {
                double estimatedTotal = elapsed.TotalSeconds / (report.Percent / 100.0);
                double remaining = estimatedTotal - elapsed.TotalSeconds;
                etaPart = remaining switch
                {
                    < 4  => string.Empty,
                    < 60 => $"~{(int)remaining} sec remaining",
                    _    => $"~{(int)(remaining / 60)}:{(int)(remaining % 60):D2} remaining"
                };
            }
        }

        var sb = new System.Text.StringBuilder(_operationStatusPrefix);
        if (speedPart.Length > 0) sb.Append($"  ·  {speedPart}");
        if (etaPart.Length > 0)   sb.Append($"  ·  {etaPart}");
        StatusMessage = sb.ToString();
    }

    private bool CanArchive() => !IsBusy && FileItems.Count > 0;
    private bool CanExtract() => !IsBusy && FileItems.Count > 0;
    private bool CanOperate() => !IsBusy;

    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            if (!FileItems.Any(x => x.FullPath == path))
                FileItems.Add(new FileItem(path));
    }

    public void AddPathsFromProtocolUri(string rawUri)
    {
        try
        {
            var uri = new Uri(rawUri);
            var query = uri.Query.TrimStart('?');
            string? base64 = null;
            foreach (var part in query.Split('&'))
            {
                var idx = part.IndexOf('=');
                if (idx > 0 && part[..idx] == "files")
                {
                    base64 = Uri.UnescapeDataString(part[(idx + 1)..]);
                    break;
                }
            }
            if (string.IsNullOrEmpty(base64)) return;
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var files = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
            if (files is not null)
            {
                AddPaths(files);
                RequestedOperation = uri.Host switch
                {
                    "extract" => "extract",
                    "archive" => "archive",
                    _ => RequestedOperation
                };
            }
        }
        catch
        {
            // Malformed URI — open normally with empty state
        }
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
