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
using Archiver.App.Core;
using Archiver.App.Models;
using Archiver.App.Services;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.Resources;

namespace Archiver.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly ResourceLoader _res = ResourceLoader.GetForViewIndependentUse();

    private readonly IArchiveService _archiveService;
    private readonly IExtractionRouter _extractionRouter;
    private readonly IArchiveListingRouter _archiveListingRouter;
    private readonly IDialogService _dialogService;
    private readonly ILogService _logService;

    private IReadOnlyDictionary<string, IReadOnlyList<ArchiveEntryViewModel>> _archiveIndex =
        new Dictionary<string, IReadOnlyList<ArchiveEntryViewModel>>();

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
    [NotifyCanExecuteChangedFor(nameof(ExtractAllFromBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractSelectedFromBrowserCommand))]
    [NotifyPropertyChangedFor(nameof(IsOperationRunning))]
    [NotifyPropertyChangedFor(nameof(IsOperationRunningVisibility))]
    [NotifyPropertyChangedFor(nameof(ArchiveButtonText))]
    [NotifyPropertyChangedFor(nameof(ExtractButtonText))]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(IsArchiveNameAndNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(NavigateDestinationUpCommand))]
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

    public string ArchiveButtonText => IsBusy && _lastOperation == "archive"
        ? _res.GetString("StatusArchiving")
        : _res.GetString("ArchiveButtonLabel");
    public string ExtractButtonText => IsBusy && _lastOperation == "extract"
        ? _res.GetString("StatusExtracting")
        : _res.GetString("ExtractButtonLabel");

    public Visibility IsOperationRunningVisibility =>
        IsBusy ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    private string _statusMessage = _res.GetString("StatusReady");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateDestinationUpCommand))]
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

    // T-F85: recognized archive extensions whose Extract path now actually works (ZIP directly,
    // the rest via ITarService/ExtractionRouter). Extension-based, not ArchiveFormatDetector's
    // magic-byte sniff — this is read on every FileItems change and must not do per-file disk
    // I/O; FileItem.Type is already a plain string computed once at construction.
    private static readonly HashSet<string> _extractableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ZIP", "RAR", "7Z", "TAR", "GZ", "TGZ", "BZ2", "TBZ2", "XZ", "TXZ", "ZST", "TZST", "LZMA"
    };

    // T-F77: only a selection that is entirely recognized archives counts as extract-only — a
    // single non-archive item means "archive everything together" is still the coherent action,
    // so the archive-only fields (Mode/Name/Compression) must stay visible for any mixed selection.
    public bool IsExtractOnlySelection =>
        FileItems.Count > 0 && FileItems.All(x => _extractableTypes.Contains(x.Type));

    // T-F05: both force-collapse while the archive browser is open — neither the batch
    // Archive/Extract outcome subtitle nor the Archive Mode/Name options have meaning once the
    // window has swapped into browsing a single archive's contents.
    public Visibility ArchiveOptionsVisibility =>
        !IsBrowsingArchive && !IsExtractOnlySelection ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OperationOutcomeVisibility =>
        !IsBrowsingArchive && FileItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string OperationOutcomeText => IsExtractOnlySelection
        ? _res.GetString("OutcomeWillExtract").Replace("{0}", FileItems.Count.ToString())
        : _res.GetString("OutcomeWillArchive").Replace("{0}", FileItems.Count.ToString());

    // T-F82: accent styling must track the resolved action (matching OperationOutcomeText),
    // not always sit on Archive — otherwise the visually-primary button can contradict what the
    // outcome subtitle says is about to happen for an extract-only selection.
    public Style? ArchiveButtonStyle =>
        IsExtractOnlySelection ? null : (Style)Application.Current.Resources["AccentButtonStyle"];

    public Style? ExtractButtonStyle =>
        IsExtractOnlySelection ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnConflictIndex))]
    private ConflictBehavior _onConflict = ConflictBehavior.Rename;

    public int OnConflictIndex
    {
        get => OnConflict switch
        {
            ConflictBehavior.Overwrite => 0,
            ConflictBehavior.Rename    => 2,
            ConflictBehavior.Ask       => 3,
            _                          => 1 // Skip
        };
        set => OnConflict = value switch
        {
            0 => ConflictBehavior.Overwrite,
            2 => ConflictBehavior.Rename,
            3 => ConflictBehavior.Ask,
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

    // T-F05: Archive Browser — inline mode-swap state. IsBrowsingArchive drives which of the two
    // Row-1/Row-3 sibling Grids in MainWindow.xaml is visible; nothing else in this ViewModel
    // changes shape based on it (destination path, OnConflict, Open/Delete-after checkboxes all
    // stay live in both modes, per the design in TASKS.md's T-F05 entry).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPendingListVisibility))]
    [NotifyPropertyChangedFor(nameof(IsBrowsingArchiveVisibility))]
    [NotifyPropertyChangedFor(nameof(ArchiveOptionsVisibility))]
    [NotifyPropertyChangedFor(nameof(OperationOutcomeVisibility))]
    [NotifyCanExecuteChangedFor(nameof(ExtractAllFromBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractSelectedFromBrowserCommand))]
    private bool _isBrowsingArchive = false;

    public Visibility IsPendingListVisibility =>
        IsBrowsingArchive ? Visibility.Collapsed : Visibility.Visible;

    public Visibility IsBrowsingArchiveVisibility =>
        IsBrowsingArchive ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtractAllFromBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractSelectedFromBrowserCommand))]
    private string? _browsedArchivePath;

    [ObservableProperty]
    private string _currentFolderPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ArchiveEntryViewModel> _currentFolderEntries = [];

    [ObservableProperty]
    private ObservableCollection<string> _breadcrumbSegments = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtractSelectedFromBrowserCommand))]
    private IReadOnlyList<ArchiveEntryViewModel> _selectedBrowserEntries = [];

    private string _sortColumn = "Name";
    private bool _sortAscending = true;

    public MainViewModel(
        IArchiveService archiveService,
        IExtractionRouter extractionRouter,
        IArchiveListingRouter archiveListingRouter,
        IDialogService dialogService,
        ILogService logService)
    {
        _archiveService = archiveService;
        _extractionRouter = extractionRouter;
        _archiveListingRouter = archiveListingRouter;
        _dialogService = dialogService;
        _logService = logService;
        _fileItems.CollectionChanged += (_, _) =>
        {
            ArchiveCommand.NotifyCanExecuteChanged();
            ExtractCommand.NotifyCanExecuteChanged();
            UpdateDefaultDestination();
            OnPropertyChanged(nameof(IsFileListEmpty));
            OnPropertyChanged(nameof(IsFileListEmptyVisibility));
            OnPropertyChanged(nameof(IsExtractOnlySelection));
            OnPropertyChanged(nameof(ArchiveOptionsVisibility));
            OnPropertyChanged(nameof(OperationOutcomeVisibility));
            OnPropertyChanged(nameof(OperationOutcomeText));
            OnPropertyChanged(nameof(ArchiveButtonStyle));
            OnPropertyChanged(nameof(ExtractButtonStyle));
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
            // Sorting by CRC clusters identical values together — a real use (spotting duplicate-
            // content files), not just a placeholder. Still-computing/unavailable (null) sorts as
            // 0, same tie-break precedent SizeBytes' -1-while-loading already sets for folders.
            "Crc"      => _sortAscending ? FileItems.OrderBy(x => x.Crc32 ?? 0) : FileItems.OrderByDescending(x => x.Crc32 ?? 0),
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

    // Path.GetDirectoryName returns null at a drive root (e.g. "C:\") and for an unrooted path —
    // both cases mean "cannot go higher," so CanNavigateDestinationUp doubles as the disable
    // condition and the click handler's own guard (added per user request, alongside the archive
    // browser's own up/exit affordance).
    private bool CanNavigateDestinationUp() => !IsBusy && Path.GetDirectoryName(DestinationPath) is not null;

    [RelayCommand(CanExecute = nameof(CanNavigateDestinationUp))]
    private void NavigateDestinationUp()
    {
        var parent = Path.GetDirectoryName(DestinationPath);
        if (parent is not null)
            DestinationPath = parent;
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
                ResolveConflictAsync = _dialogService.ShowConflictDialogAsync,
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
                await RunCleanupAsync(GetDeletableSources(options.SourcePaths, result));
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
        }
        // T-F70: IsBusy stays true for as long as something transient is still on screen — a
        // modal dialog for success/issues/error (awaited above, inside the try), or this delay
        // for cancel — so a new operation can never start while the previous outcome is still
        // being shown. Keeping this consistent across all four outcomes was a deliberate choice;
        // see DECISIONS.md.
        if (wasCancelled)
        {
            await Task.Delay(2000);
        }
        IsBusy = false;
        StatusMessage = _res.GetString("StatusReady");
    }

    [RelayCommand(CanExecute = nameof(CanExtract))]
    private Task ExtractAsync() =>
        RunExtractAsync([.. FileItems.Select(x => x.FullPath)], selectedEntryPaths: null);

    // T-F05: shared by the whole-archive Extract button and the archive browser's Extract
    // Selected/Extract All/double-click-a-file commands below — the entire IsBusy/progress/
    // stopwatch/bomb-confirm-callback/summary-dialog/cleanup sequence stays identical for both;
    // only which archive(s) and which entry subset (if any) get passed to ExtractOptions differ.
    private async Task RunExtractAsync(IReadOnlyList<string> archivePaths, IReadOnlyList<string>? selectedEntryPaths)
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
                ArchivePaths = archivePaths,
                DestinationFolder = DestinationPath,
                OnConflict = OnConflict,
                OpenDestinationFolder = OpenDestinationFolder,
                DeleteArchiveAfterExtraction = DeleteAfterOperation,
                ConfirmCompressionBombExtraction = _dialogService.ShowCompressionBombConfirmAsync,
                ResolveConflictAsync = _dialogService.ShowConflictDialogAsync,
                SelectedEntryPaths = selectedEntryPaths,
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

            var result = await _extractionRouter.ExtractAsync(options, progress, _cts.Token);
            if (result.Success && DeleteAfterOperation)
                await RunCleanupAsync(GetDeletableSources(options.ArchivePaths, result));
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
        }
        // T-F70: see the matching comment in ArchiveAsync — IsBusy stays true until whatever
        // transient thing is on screen (dialog or this delay) is gone, for all four outcomes.
        if (wasCancelled)
        {
            await Task.Delay(2000);
        }
        IsBusy = false;
        StatusMessage = _res.GetString("StatusReady");
    }

    [RelayCommand(CanExecute = nameof(IsOperationRunning))]
    private void Cancel() => _cts?.Cancel();

    // ── Archive Browser (T-F05) ──────────────────────────────────────────────────

    public async Task EnterBrowseModeAsync(string archivePath)
    {
        IsBrowsingArchive = true;
        BrowsedArchivePath = archivePath;
        CurrentFolderPath = string.Empty;
        SelectedBrowserEntries = [];

        // tar-family listing shells out to tar.exe per T-F49/T-F48's model; ZIP listing is fast
        // in-memory (ZipFile.OpenRead). Only show the async-load indeterminate state for the
        // former, matching T-F58's existing "Finalizing..." pattern rather than a blocking modal.
        bool isZip = ArchiveFormatDetector.Detect(archivePath) is ArchiveFormat.Zip or ArchiveFormat.Unknown;
        if (!isZip)
        {
            IsProgressIndeterminate = true;
            StatusMessage = _res.GetString("StatusFinalizing");
        }

        ArchiveListResult result;
        try
        {
            result = await _archiveListingRouter.ListEntriesAsync(archivePath);
        }
        finally
        {
            IsProgressIndeterminate = false;
            StatusMessage = _res.GetString("StatusReady");
        }

        if (!result.Success)
        {
            IsBrowsingArchive = false;
            BrowsedArchivePath = null;
            await _dialogService.ShowErrorAsync("Error", result.ErrorMessage ?? "Failed to read archive.");
            return;
        }

        _archiveIndex = ArchiveTreeIndex.Build(result.Entries);
        RefreshCurrentFolder();
    }

    private void RefreshCurrentFolder()
    {
        // T-F05: a childless folder (explicit empty directory entry) is a node in its parent's
        // child list but has no key of its own in the index — TryGetValue + empty fallback avoids
        // a KeyNotFoundException on navigating into one, rather than assuming every folder path
        // is guaranteed a dictionary entry.
        var entries = _archiveIndex.TryGetValue(CurrentFolderPath, out var list)
            ? list
            : (IReadOnlyList<ArchiveEntryViewModel>)[];
        CurrentFolderEntries = new ObservableCollection<ArchiveEntryViewModel>(entries);
        SelectedBrowserEntries = [];
        RebuildBreadcrumb();
    }

    private void RebuildBreadcrumb()
    {
        string archiveName = Path.GetFileName(BrowsedArchivePath ?? string.Empty);
        var segments = new List<string> { archiveName };
        if (CurrentFolderPath.Length > 0)
            segments.AddRange(CurrentFolderPath.Split('/'));
        BreadcrumbSegments = new ObservableCollection<string>(segments);
    }

    public void NavigateIntoFolder(ArchiveEntryViewModel folder)
    {
        if (!folder.IsFolder) return;
        CurrentFolderPath = folder.FullPath;
        RefreshCurrentFolder();
    }

    // index 0 = archive root (BreadcrumbSegments[0], the archive's own file name).
    public void NavigateToBreadcrumbSegment(int index)
    {
        if (index <= 0)
        {
            CurrentFolderPath = string.Empty;
        }
        else
        {
            var segments = CurrentFolderPath.Split('/');
            CurrentFolderPath = string.Join('/', segments.Take(index));
        }
        RefreshCurrentFolder();
    }

    public void SetSelectedBrowserEntries(IReadOnlyList<ArchiveEntryViewModel> entries) =>
        SelectedBrowserEntries = entries;

    // Single "up" affordance for the whole Archive Browser (design review 2026-07-13, follow-up):
    // replaces the standalone Close button. Inside the archive, it steps up one folder level
    // (same target as clicking the second-to-last breadcrumb segment); at the archive's own root
    // there is nowhere higher to go within the archive, so it falls through to exiting the browser
    // back to the pending list — the same behavior the removed Close button had.
    [RelayCommand]
    private void NavigateUpOrExitBrowser()
    {
        if (CurrentFolderPath.Length == 0)
        {
            ExitBrowseMode();
            return;
        }
        NavigateToBreadcrumbSegment(BreadcrumbSegments.Count - 2);
    }

    [RelayCommand]
    private void ExitBrowseMode()
    {
        IsBrowsingArchive = false;
        BrowsedArchivePath = null;
        CurrentFolderPath = string.Empty;
        CurrentFolderEntries = [];
        BreadcrumbSegments = [];
        SelectedBrowserEntries = [];
        _archiveIndex = new Dictionary<string, IReadOnlyList<ArchiveEntryViewModel>>();
    }

    private bool CanExtractSelectedFromBrowser() =>
        !IsBusy && BrowsedArchivePath is not null && SelectedBrowserEntries.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExtractSelectedFromBrowser))]
    private Task ExtractSelectedFromBrowserAsync() =>
        RunExtractAsync([BrowsedArchivePath!], [.. SelectedBrowserEntries.Select(e => e.FullPath)]);

    private bool CanExtractAllFromBrowser() => !IsBusy && BrowsedArchivePath is not null;

    [RelayCommand(CanExecute = nameof(CanExtractAllFromBrowser))]
    private Task ExtractAllFromBrowserAsync() =>
        RunExtractAsync([BrowsedArchivePath!], selectedEntryPaths: null);

    // Double-click a file row in the browser view — extracts just that one entry, reusing the
    // same RunExtractAsync sequence (bomb-confirm callback, progress, summary dialog) as every
    // other extraction path.
    public Task ExtractSingleBrowserEntryAsync(ArchiveEntryViewModel entry) =>
        RunExtractAsync([BrowsedArchivePath!], [entry.FullPath]);

    // T-F87: a source whose full path appears in SkippedFiles was never actually archived or
    // extracted (unsupported format, whole-archive conflict skip, or every entry individually
    // skipped) — deleting it with DeleteAfterOperation on would be data loss. Per-entry skips
    // record an entry's relative path, not a source's full path, so they never collide with this
    // filter — only a whole-source skip (Path == one of `sources`) excludes that source here.
    private static IEnumerable<string> GetDeletableSources(IReadOnlyList<string> sources, ArchiveResult result)
    {
        var skipped = new HashSet<string>(result.SkippedFiles.Select(s => s.Path), StringComparer.OrdinalIgnoreCase);
        return sources.Where(p => !skipped.Contains(p));
    }

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
