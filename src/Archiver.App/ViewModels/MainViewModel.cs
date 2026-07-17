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

// T-F107: what CurrentFolderPath/CurrentFolderEntries/the breadcrumb mean, and where "Up" goes
// next. Archive = browsing inside the currently open archive (CurrentFolderPath is '/'-separated,
// archive-relative). RealFileSystem = browsing a real Windows folder (CurrentFolderPath is an
// absolute Windows path). ThisPc = the synthetic drives-list root; CurrentFolderPath is unused.
// Public (not nested-private) so MainWindow.xaml.cs's double-tap handler can dispatch on it too.
public enum ArchiveBrowseScope
{
    Archive,
    RealFileSystem,
    ThisPc,
}

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly ResourceLoader _res = ResourceLoader.GetForViewIndependentUse();

    private readonly IArchiveCreationRouter _archiveCreationRouter;
    private readonly IExtractionRouter _extractionRouter;
    private readonly IArchiveListingRouter _archiveListingRouter;
    private readonly IDialogService _dialogService;
    private readonly ILogService _logService;

    private IReadOnlyDictionary<string, IReadOnlyList<ArchiveEntryViewModel>> _archiveIndex =
        new Dictionary<string, IReadOnlyList<ArchiveEntryViewModel>>();

    // T-F98: nested archive drill-down. Each pushed frame is the PARENT level's state, restored
    // when the user navigates back up out of the currently-open (child) nested archive's own
    // root. _currentNestedScopeDir is the current level's own NestedArchiveCache folder (null for
    // the real, top-level archive — nothing to clean up there). _nestedBreadcrumbAncestry holds
    // every ancestor level's contribution to the breadcrumb (root name + folder path at the point
    // it was left); _currentLevelDisplayName overrides RebuildBreadcrumb's usual
    // Path.GetFileName(BrowsedArchivePath) for a nested level, whose BrowsedArchivePath is an
    // ugly temp-extracted file, not the real entry name the user drilled into.
    private sealed record NestedBrowseLevel(
        string? ArchivePath,
        string CurrentFolderPath,
        string? DisplayName,
        IReadOnlyDictionary<string, IReadOnlyList<ArchiveEntryViewModel>> ArchiveIndex,
        List<string> BreadcrumbAncestry,
        string? ScopeDir);

    private readonly Stack<NestedBrowseLevel> _browseStack = new();
    private string? _currentNestedScopeDir;
    private string? _currentLevelDisplayName;
    private List<string> _nestedBreadcrumbAncestry = [];

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
    [NotifyPropertyChangedFor(nameof(IsCompressionLevelEnabled))]
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

    // T-F77: only a selection that is entirely recognized archives counts as extract-only — a
    // single non-archive item means "archive everything together" is still the coherent action,
    // so the archive-only fields (Mode/Name/Compression) must stay visible for any mixed selection.
    // T-F98: uses ArchiveFormatDetector.IsRecognizedArchiveExtension (extension-based, not the
    // magic-byte Detect() sniff — this is read on every FileItems change and must not do
    // per-file disk I/O) instead of a second, separately-maintained extension list.
    public bool IsExtractOnlySelection =>
        FileItems.Count > 0 && FileItems.All(x => ArchiveFormatDetector.IsRecognizedArchiveExtension(x.FullPath));

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

    // T-F105: the container/compression format to create the archive as. Plain Tar is the one
    // format where tar.exe's compression-level flag is genuinely inapplicable (`--options
    // gzip:compression-level=N` fails with "Unknown module name" when no filter is active —
    // confirmed empirically, see DECISIONS.md's T-F105 entry) — every other format, including
    // ZIP and all 5 compressed tar variants, keeps the compression-level control live.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatIndex))]
    [NotifyPropertyChangedFor(nameof(IsCompressionLevelEnabled))]
    private ArchiveContainerFormat _selectedContainerFormat = ArchiveContainerFormat.Zip;

    public int FormatIndex
    {
        get => SelectedContainerFormat switch
        {
            ArchiveContainerFormat.Zip     => 0,
            ArchiveContainerFormat.Tar     => 1,
            ArchiveContainerFormat.TarGz   => 2,
            ArchiveContainerFormat.TarBz2  => 3,
            ArchiveContainerFormat.TarXz   => 4,
            ArchiveContainerFormat.TarZst  => 5,
            ArchiveContainerFormat.TarLzma => 6,
            _                              => 0
        };
        set => SelectedContainerFormat = value switch
        {
            0 => ArchiveContainerFormat.Zip,
            1 => ArchiveContainerFormat.Tar,
            2 => ArchiveContainerFormat.TarGz,
            3 => ArchiveContainerFormat.TarBz2,
            4 => ArchiveContainerFormat.TarXz,
            5 => ArchiveContainerFormat.TarZst,
            6 => ArchiveContainerFormat.TarLzma,
            _ => ArchiveContainerFormat.Zip
        };
    }

    public bool IsPlainTarFormatSelected => SelectedContainerFormat == ArchiveContainerFormat.Tar;

    public bool IsCompressionLevelEnabled => IsNotBusy && !IsPlainTarFormatSelected;

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
    [NotifyCanExecuteChangedFor(nameof(NavigateUpCommand))]
    private ArchiveBrowseScope _browseScope = ArchiveBrowseScope.Archive;

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
        IArchiveCreationRouter archiveCreationRouter,
        IExtractionRouter extractionRouter,
        IArchiveListingRouter archiveListingRouter,
        IDialogService dialogService,
        ILogService logService)
    {
        _archiveCreationRouter = archiveCreationRouter;
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
    // browser's own up-navigation affordance — a separate command for a separate row; see
    // DECISIONS.md's T-F107 entry).
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
                Format = SelectedContainerFormat,
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

            var result = await _archiveCreationRouter.ArchiveAsync(options, progress, _cts.Token);
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
    private async Task RunExtractAsync(IReadOnlyList<string> archivePaths, IReadOnlyList<string>? selectedEntryPaths, string? destinationOverride = null)
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
                // T-F109: destinationOverride lets a caller (the unsafe-preview-type warning
                // flow) land a single-entry extraction next to the archive instead of whatever
                // the user's Destination field currently holds — that field is for deliberate
                // bulk Extract Selected/All operations, not a one-off security-gated extraction.
                DestinationFolder = destinationOverride ?? DestinationPath,
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
        BrowseScope = ArchiveBrowseScope.Archive;
        BrowsedArchivePath = archivePath;
        CurrentFolderPath = string.Empty;
        SelectedBrowserEntries = [];
        ResetNestedBrowseStack();

        // Bug found 2026-07-17: entering browse mode via file activation (T-F100) or by
        // double-clicking a real archive found while browsing real folders (T-F107) never goes
        // through AddPaths, so FileItems stays empty and UpdateDefaultDestination() (only wired
        // to FileItems.CollectionChanged) never fires — DestinationPath then silently stays at
        // its Desktop default regardless of where the archive actually lives. Only apply this
        // when FileItems is empty — the pending-list double-click entry point (T-F05's original
        // flow) already got a correct destination from UpdateDefaultDestination() when the
        // archive was added, and a user may have since picked a different one deliberately.
        if (FileItems.Count == 0)
            DestinationPath = Path.GetDirectoryName(archivePath) ?? DestinationPath;

        // T-F106: this is awaited un-awaited (fire-and-forget) from App.xaml.cs's deferred
        // activation path — a thrown exception there would otherwise leave IsBrowsingArchive
        // stuck true with no archive index and no visible error (found via code-review advisor
        // pass). ListArchiveWithProgressAsync catching internally, not just at the activation
        // call site, fixes it for every caller, matching the same reset+dialog recovery already
        // used for result.Success==false below.
        ArchiveListResult? result = await ListArchiveWithProgressAsync(archivePath);
        if (result is null)
        {
            IsBrowsingArchive = false;
            BrowsedArchivePath = null;
            return;
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

    // T-F98: shared by EnterBrowseModeAsync (a real, on-disk archive) and
    // NavigateIntoNestedArchiveAsync (a temp-extracted nested one) — tar-family listing shells
    // out to tar.exe per T-F49/T-F48's model, ZIP listing is fast in-memory (ZipFile.OpenRead);
    // only show the async-load indeterminate state for the former, matching T-F58's existing
    // "Finalizing..." pattern rather than a blocking modal. Returns null (already reported to the
    // user) on any exception — never throws to its caller.
    private async Task<ArchiveListResult?> ListArchiveWithProgressAsync(string archivePath)
    {
        bool isZip = ArchiveFormatDetector.Detect(archivePath) is ArchiveFormat.Zip or ArchiveFormat.Unknown;
        if (!isZip)
        {
            IsProgressIndeterminate = true;
            StatusMessage = _res.GetString("StatusFinalizing");
        }

        try
        {
            return await _archiveListingRouter.ListEntriesAsync(archivePath);
        }
        catch (Exception ex)
        {
            _logService.Error("Archive listing failed", ex);
            await _dialogService.ShowErrorAsync("Error", ex.Message);
            return null;
        }
        finally
        {
            IsProgressIndeterminate = false;
            StatusMessage = _res.GetString("StatusReady");
        }
    }

    // T-F98: defensive reset — by the time a fresh EnterBrowseModeAsync runs, the nested browse
    // stack should already be empty (NavigateUp only reaches RealFileSystem/ThisPc scope once
    // every nested level has been popped), but clearing it here and deleting any scope dirs it
    // still holds means a future bug leaks a stale stack, not an ever-growing pile of temp folders.
    private void ResetNestedBrowseStack()
    {
        if (_currentNestedScopeDir is not null)
            NestedArchiveCache.DeleteScope(_currentNestedScopeDir);
        while (_browseStack.Count > 0)
        {
            var level = _browseStack.Pop();
            if (level.ScopeDir is not null)
                NestedArchiveCache.DeleteScope(level.ScopeDir);
        }
        _currentNestedScopeDir = null;
        _currentLevelDisplayName = null;
        _nestedBreadcrumbAncestry = [];
    }

    // T-F98: double-clicking an archive entry found inside the currently open archive extracts
    // just that one entry to a fresh NestedArchiveCache scope and browses it, recursively — see
    // DECISIONS.md's T-F98 entry for the depth limit and per-level security reasoning. Reuses the
    // same single-entry extraction shape as PreviewBrowserEntryAsync (T-F97), so T-F49's
    // whole-archive pre-scan and T-F90/T-F94's compression-bomb + disk-space check both still run
    // unmodified, scoped to whichever archive is being extracted at this level.
    public async Task NavigateIntoNestedArchiveAsync(ArchiveEntryViewModel entry)
    {
        if (BrowsedArchivePath is null) return;

        if (NestedArchivePolicy.ExceedsMaxDepth(_browseStack.Count))
        {
            await _dialogService.ShowErrorAsync("Error", _res.GetString("NestedArchiveDepthLimitReached"));
            return;
        }

        string scopeDir = NestedArchiveCache.CreateScope();
        var options = new ExtractOptions
        {
            ArchivePaths = [BrowsedArchivePath],
            DestinationFolder = scopeDir,
            Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = [entry.FullPath],
            ConfirmCompressionBombExtraction = _dialogService.ShowCompressionBombConfirmAsync,
        };

        StatusMessage = _res.GetString("StatusOpening");
        ArchiveResult result;
        try
        {
            result = await _extractionRouter.ExtractAsync(options);
        }
        finally
        {
            StatusMessage = _res.GetString("StatusReady");
        }

        if (!result.Success || result.CreatedFiles.Count == 0)
        {
            NestedArchiveCache.DeleteScope(scopeDir);
            await _dialogService.ShowErrorAsync("Error", _res.GetString("StatusIssues"));
            return;
        }

        // The entry may itself not really be an archive despite its extension (ArchiveFormatDetector
        // couldn't check this before extraction — its magic-byte sniff needs a real file on disk).
        // Confirm now, mirroring EnterBrowseModeAsync's own "detect what you actually got" posture.
        string extractedPath = Path.Combine(scopeDir, entry.FullPath.Replace('/', Path.DirectorySeparatorChar));
        if (ArchiveFormatDetector.Detect(extractedPath) == ArchiveFormat.Unknown)
        {
            NestedArchiveCache.DeleteScope(scopeDir);
            await _dialogService.ShowErrorAsync("Error", _res.GetString("StatusIssues"));
            return;
        }

        ArchiveListResult? listResult = await ListArchiveWithProgressAsync(extractedPath);
        if (listResult is null || !listResult.Success)
        {
            NestedArchiveCache.DeleteScope(scopeDir);
            if (listResult is not null)
                await _dialogService.ShowErrorAsync("Error", listResult.ErrorMessage ?? "Failed to read archive.");
            return;
        }

        _browseStack.Push(new NestedBrowseLevel(
            BrowsedArchivePath,
            CurrentFolderPath,
            _currentLevelDisplayName,
            _archiveIndex,
            new List<string>(_nestedBreadcrumbAncestry),
            _currentNestedScopeDir));

        _nestedBreadcrumbAncestry.Add(_currentLevelDisplayName ?? Path.GetFileName(BrowsedArchivePath ?? string.Empty));
        if (CurrentFolderPath.Length > 0)
            _nestedBreadcrumbAncestry.AddRange(CurrentFolderPath.Split('/'));

        _currentLevelDisplayName = entry.Name;
        _currentNestedScopeDir = scopeDir;
        BrowsedArchivePath = extractedPath;
        CurrentFolderPath = string.Empty;
        _archiveIndex = ArchiveTreeIndex.Build(listResult.Entries);
        RefreshCurrentFolder();
    }

    private void RefreshCurrentFolder()
    {
        // T-F05/T-F107: a childless archive folder (explicit empty directory entry) is a node in
        // its parent's child list but has no key of its own in the index — TryGetValue + empty
        // fallback avoids a KeyNotFoundException on navigating into one, rather than assuming
        // every folder path is guaranteed a dictionary entry.
        IReadOnlyList<ArchiveEntryViewModel> entries = BrowseScope switch
        {
            ArchiveBrowseScope.RealFileSystem => FileSystemBrowser.ListFolder(CurrentFolderPath),
            ArchiveBrowseScope.ThisPc => FileSystemBrowser.ListDrives(),
            _ => _archiveIndex.TryGetValue(CurrentFolderPath, out var list) ? list : [],
        };
        CurrentFolderEntries = new ObservableCollection<ArchiveEntryViewModel>(entries);
        SelectedBrowserEntries = [];
        RebuildBreadcrumb();
    }

    private void RebuildBreadcrumb()
    {
        var segments = new List<string>();
        switch (BrowseScope)
        {
            case ArchiveBrowseScope.ThisPc:
                segments.Add(_res.GetString("ThisPcBreadcrumbRoot"));
                break;
            case ArchiveBrowseScope.RealFileSystem:
                segments.Add(_res.GetString("ThisPcBreadcrumbRoot"));
                segments.AddRange(CurrentFolderPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
                break;
            default: // Archive
                // T-F98: ancestry holds every enclosing nested level's own contribution; this
                // level's own root uses _currentLevelDisplayName (the entry name it was entered
                // as) when set, since BrowsedArchivePath for a nested level is a temp-extracted
                // file, not the name the user actually drilled into.
                segments.AddRange(_nestedBreadcrumbAncestry);
                segments.Add(_currentLevelDisplayName ?? Path.GetFileName(BrowsedArchivePath ?? string.Empty));
                if (CurrentFolderPath.Length > 0)
                    segments.AddRange(CurrentFolderPath.Split('/'));
                break;
        }
        BreadcrumbSegments = new ObservableCollection<string>(segments);
    }

    public void NavigateIntoFolder(ArchiveEntryViewModel folder)
    {
        if (!folder.IsFolder) return;
        if (BrowseScope != ArchiveBrowseScope.Archive)
            BrowseScope = ArchiveBrowseScope.RealFileSystem; // a drive entry clicked from ThisPc
        CurrentFolderPath = folder.FullPath;
        RefreshCurrentFolder();
    }

    // Breadcrumb index 0 = archive root (Archive scope) or the synthetic "This PC" segment
    // (RealFileSystem/ThisPc scope) — never both, since the breadcrumb is rebuilt fresh on every
    // scope transition and only ever shows segments for the currently active scope.
    public void NavigateToBreadcrumbSegment(int index)
    {
        switch (BrowseScope)
        {
            case ArchiveBrowseScope.ThisPc:
                break; // only one segment ever exists ("This PC") — nothing to do.

            case ArchiveBrowseScope.RealFileSystem:
                if (index <= 0)
                {
                    BrowseScope = ArchiveBrowseScope.ThisPc;
                    CurrentFolderPath = string.Empty;
                }
                else
                {
                    var segments = CurrentFolderPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                    // index 1 = the drive segment alone, which needs a trailing separator to mean
                    // the drive's root ("C:" means "current directory on C:" in .NET, not "C:\").
                    CurrentFolderPath = index == 1
                        ? segments[0] + Path.DirectorySeparatorChar
                        : string.Join(Path.DirectorySeparatorChar, segments.Take(index));
                }
                break;

            default: // Archive
                // T-F98: index is global across the whole (ancestry + this level's own) breadcrumb;
                // an ancestor segment (belonging to a previous nesting level) isn't directly
                // clickable — reaching it means popping one or more nested levels, which Up
                // already does correctly one level at a time. See DECISIONS.md's T-F98 entry.
                int localIndex = index - _nestedBreadcrumbAncestry.Count;
                if (localIndex < 0)
                {
                    return;
                }
                else if (localIndex == 0)
                {
                    CurrentFolderPath = string.Empty;
                }
                else
                {
                    var segments = CurrentFolderPath.Split('/');
                    CurrentFolderPath = string.Join('/', segments.Take(localIndex));
                }
                break;
        }
        RefreshCurrentFolder();
    }

    public void SetSelectedBrowserEntries(IReadOnlyList<ArchiveEntryViewModel> entries) =>
        SelectedBrowserEntries = entries;

    // T-F107: single "up" affordance for the whole Archive Browser. Inside the archive, steps up
    // one folder level. At the archive's own root, keeps climbing into real Windows folders — the
    // archive's containing folder, then real parent folders, up to a drive root, up to the
    // synthetic "This PC" drives list. Never exits the browser back to the pending list anymore
    // (see DECISIONS.md's T-F107 entry) — the window's own close button covers that; CanNavigateUp
    // disables this button only at "This PC", mirroring CanNavigateDestinationUp's drive-root
    // disable pattern.
    private bool CanNavigateUp() => BrowseScope != ArchiveBrowseScope.ThisPc;

    [RelayCommand(CanExecute = nameof(CanNavigateUp))]
    private void NavigateUp()
    {
        switch (BrowseScope)
        {
            case ArchiveBrowseScope.Archive:
                if (CurrentFolderPath.Length == 0)
                {
                    // T-F98: a nested level's own root pops back to its parent level instead of
                    // falling through to the real-filesystem climb — only once every nested level
                    // has been popped does the outermost (real, on-disk) archive's own root reach
                    // the RealFileSystem/ThisPc climb below, unchanged from T-F107.
                    if (_browseStack.Count > 0)
                    {
                        string? childScopeDir = _currentNestedScopeDir;
                        var parentLevel = _browseStack.Pop();
                        BrowsedArchivePath = parentLevel.ArchivePath;
                        CurrentFolderPath = parentLevel.CurrentFolderPath;
                        _currentLevelDisplayName = parentLevel.DisplayName;
                        _archiveIndex = parentLevel.ArchiveIndex;
                        _nestedBreadcrumbAncestry = parentLevel.BreadcrumbAncestry;
                        _currentNestedScopeDir = parentLevel.ScopeDir;
                        RefreshCurrentFolder();
                        if (childScopeDir is not null)
                            NestedArchiveCache.DeleteScope(childScopeDir);
                    }
                    else
                    {
                        string? containingFolder = Path.GetDirectoryName(BrowsedArchivePath);
                        BrowsedArchivePath = null;
                        if (containingFolder is not null)
                        {
                            BrowseScope = ArchiveBrowseScope.RealFileSystem;
                            CurrentFolderPath = containingFolder;
                        }
                        else
                        {
                            BrowseScope = ArchiveBrowseScope.ThisPc;
                            CurrentFolderPath = string.Empty;
                        }
                        RefreshCurrentFolder();
                    }
                }
                else
                {
                    NavigateToBreadcrumbSegment(BreadcrumbSegments.Count - 2);
                }
                break;

            case ArchiveBrowseScope.RealFileSystem:
                string? parent = Path.GetDirectoryName(CurrentFolderPath);
                if (parent is not null)
                {
                    CurrentFolderPath = parent;
                }
                else
                {
                    BrowseScope = ArchiveBrowseScope.ThisPc;
                    CurrentFolderPath = string.Empty;
                }
                RefreshCurrentFolder();
                break;

            case ArchiveBrowseScope.ThisPc:
                break; // CanNavigateUp() is false here — unreachable in practice.
        }
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

    // T-F109: double-clicking a file type outside PreviewPolicy's allowlist is a real security
    // boundary (see SECURITY.md's T-F97 section), not just an inconvenience — 7-Zip/NanaZip have
    // no such allowlist at all and unconditionally ShellExecute anything, including .exe (see
    // DECISIONS.md's T-F109 entry for the real source trace). Pakko instead warns first, and on
    // confirmation extracts just that one entry into a dedicated subfolder next to the archive
    // (ArchiveNaming.GetBaseName, same helper T-F103's smart-foldering uses) — deliberately not
    // the user's Destination field, which is for bulk Extract Selected/All, not a one-off warned
    // extraction. Reuses the same RunExtractAsync sequence (bomb-confirm callback, progress,
    // summary dialog) as every other extraction path via destinationOverride.
    public async Task ExtractSingleBrowserEntryWithWarningAsync(ArchiveEntryViewModel entry)
    {
        if (BrowsedArchivePath is null) return;

        bool confirmed = await _dialogService.ShowConfirmAsync(
            _res.GetString("UnsafePreviewConfirmTitle"),
            _res.GetString("UnsafePreviewConfirmMessage").Replace("{0}", entry.Name));
        if (!confirmed) return;

        string archiveDir = Path.GetDirectoryName(BrowsedArchivePath) ?? DestinationPath;
        string destDir = Path.Combine(archiveDir, ArchiveNaming.GetBaseName(BrowsedArchivePath));
        await RunExtractAsync([BrowsedArchivePath], [entry.FullPath], destDir);
    }

    // T-F97: previewable file types (PreviewPolicy) skip the full Extract ceremony (progress,
    // summary dialog) — silently extract to a throwaway PreviewCache scope and open with the OS
    // default handler, with only a status-line change to indicate it's happening. Reuses the
    // real IExtractionRouter pipeline (not a lightweight shortcut) so T-F49's whole-archive
    // pre-scan and MOTW propagation both still apply unchanged — see DECISIONS.md's T-F97 entry.
    public async Task PreviewBrowserEntryAsync(ArchiveEntryViewModel entry)
    {
        if (BrowsedArchivePath is null) return;

        StatusMessage = _res.GetString("StatusOpening");
        try
        {
            string scopeDir = PreviewCache.CreateScope();
            var options = new ExtractOptions
            {
                ArchivePaths = [BrowsedArchivePath],
                DestinationFolder = scopeDir,
                Mode = ExtractMode.SingleFolder,
                SelectedEntryPaths = [entry.FullPath],
                ConfirmCompressionBombExtraction = _dialogService.ShowCompressionBombConfirmAsync,
            };

            var result = await _extractionRouter.ExtractAsync(options);
            if (!result.Success || result.CreatedFiles.Count == 0)
            {
                await _dialogService.ShowErrorAsync("Error",
                    result.Errors.FirstOrDefault()?.Message ?? _res.GetString("StatusIssues"));
                return;
            }

            // ArchiveResult.CreatedFiles lists per-archive destination folders, not individual
            // extracted file paths (see ZipArchiveService/TarSandboxedService) — the previewed
            // entry's actual on-disk path has to be computed from the scope dir + entry path.
            string previewFilePath = Path.Combine(scopeDir, entry.FullPath.Replace('/', Path.DirectorySeparatorChar));
            if (!await _dialogService.OpenFileWithDefaultAppAsync(previewFilePath))
                await _dialogService.ShowErrorAsync("Error", _res.GetString("StatusIssues"));
        }
        catch (Exception ex)
        {
            _logService.Error("Preview failed", ex);
            await _dialogService.ShowErrorAsync("Error", ex.Message);
        }
        finally
        {
            StatusMessage = _res.GetString("StatusReady");
        }
    }

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
