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
using Archiver.App.Services;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.Resources;

namespace Archiver.App.ViewModels;

// CA1001 (owns disposable field '_cts', isn't itself disposable): reviewed T-F137, re-reviewed
// T-F150 — every use already disposes _cts in a finally block; the only real leak window is the
// ViewModel itself being torn down mid-operation, which this app's single-window WinUI lifecycle
// never does. Adding IDisposable/Dispose() here would be dead code with no caller, not a real
// fix — see docs/CONVENTIONS.md's "Static-Analysis Won't-Fix Conventions" section.
#pragma warning disable CA1001
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly ResourceLoader _res = ResourceLoader.GetForViewIndependentUse();

    private readonly IArchiveCreationRouter _archiveCreationRouter;
    private readonly IExtractionRouter _extractionRouter;
    private readonly IArchiveListingRouter _archiveListingRouter;
    private readonly IAntivirusScanService _antivirusScanService;
    private readonly IDialogService _dialogService;
    private readonly ILogService _logService;
    private readonly GroupPolicyOptions _policy;
    private readonly SourceRecycler _sourceRecycler;
    private readonly TarCapabilities _tarCapabilities;

    // T-F200: a password that worked is remembered for the browse session only.
    private readonly SessionPasswordMemory _browsePasswords = new();
    private readonly InlinePasswordState _encryptionPassword = new();

    private ArchiveTree _archiveIndex = ArchiveTreeIndex.Build([]);

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
        ArchiveTree ArchiveIndex,
        List<string> BreadcrumbAncestry,
        string? ScopeDir,
        EncryptionSummary? Encryption,
        bool IsZip);

    private readonly Stack<NestedBrowseLevel> _browseStack = new();
    private string? _currentNestedScopeDir;
    private string? _currentLevelDisplayName;
    private List<string> _nestedBreadcrumbAncestry = [];

    // T-F199 step 6: the open archive level's encryption badge/notes and whether Test applies —
    // set on every transition (open, drill-in, back out, climb out, close).
    private EncryptionSummary? _browseEncryption;
    private bool _browsedIsZip;
    private readonly BrowseWork _browseWork = new();

    private CancellationTokenSource? _cts;

    private System.Diagnostics.Stopwatch? _operationStopwatch;
    // T-F142: the EMA-sampling arithmetic itself moved to Archiver.Core.Services.ProgressSpeedSampler
    // (shared with Archiver.Shell's dialog) — a fresh instance per operation is the reset, by
    // design (see that class's own doc comment for why there is no Reset() method instead).
    private ProgressSpeedSampler? _speedSampler;
    private string _operationStatusPrefix = string.Empty;
    // T-F307: where the time-left estimate starts counting; moved to the end of a tar archive's
    // listing passes, which take time at a standing bar.
    private TimeSpan _estimateStart;
    private int _estimateStartPercent;
    private bool _checkingArchive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractAllFromBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractSelectedFromBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanArchiveFromBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestBrowsedArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseArchiveCommand))]
    [NotifyPropertyChangedFor(nameof(IsOperationRunning))]
    [NotifyPropertyChangedFor(nameof(IsOperationRunningVisibility))]
    [NotifyPropertyChangedFor(nameof(ArchiveButtonText))]
    [NotifyPropertyChangedFor(nameof(ExtractButtonText))]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(IsArchiveNameAndNotBusy))]
    [NotifyPropertyChangedFor(nameof(IsCompressionLevelEnabled))]
    [NotifyPropertyChangedFor(nameof(DownloadMarkCanChange))]
    [NotifyCanExecuteChangedFor(nameof(NavigateDestinationUpCommand))]
    private bool _isBusy = false;

    private string _lastOperation = string.Empty;

    // T-F211: an operation starting is the next action — the previous result line goes.
    partial void OnIsBusyChanged(bool value)
    {
        if (value)
            ClearOutcome();
        RaiseFooter();
    }

    [ObservableProperty]
    private int _progress = 0;

    [ObservableProperty]
    private bool _isProgressIndeterminate = false;

    public bool IsOperationRunning => IsBusy;
    public bool IsNotBusy => !IsBusy;

    public string ArchiveButtonText => IsBusy && _lastOperation == "archive"
        ? _res.GetString("StatusArchiving")
        : string.Format(System.Globalization.CultureInfo.CurrentCulture, _res.GetString("ArchiveButtonLabel"),
            CreateModeText.FormatName(SelectedContainerFormat));
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
    [NotifyPropertyChangedFor(nameof(ArchiveNamePlaceholder))]
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

    // T-F199/T-F212: what the list allows and which action is the primary one — recomputed on every
    // list change (extension-based, no disk I/O), the same policy/tar.exe check the routers apply.
    private ListActions _listActions = PrimaryActionPolicy.Evaluate([], _ => false);

    private bool IsExtractAccent => IsBrowsingArchive || _listActions.Accent == PrimaryAction.Extract;

    // T-F360: "apply the download mark", on again for every new list or archive. Whether an archive
    // carries the mark is read off the UI thread; a result for a list that has since changed is
    // dropped (the generation).
    private bool _anyArchiveMarked;
    private int _downloadMarkGeneration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadMarkChecked))]
    [NotifyPropertyChangedFor(nameof(DownloadMarkNoteText))]
    private bool _applyDownloadMark = true;

    private DownloadMarkView MarkView => DownloadMarkOption.For(
        IsBrowsingArchive ? Location.ShowsExtractActions : _listActions.Accent == PrimaryAction.Extract,
        _anyArchiveMarked, _policy, ApplyDownloadMark);

    public Visibility DownloadMarkVisibility => MarkView.Visible ? Visibility.Visible : Visibility.Collapsed;

    public bool DownloadMarkChecked
    {
        get => MarkView.Checked;
        set => ApplyDownloadMark = value;
    }

    public bool DownloadMarkCanChange => MarkView.CanChange && !IsBusy;

    public string DownloadMarkNoteText => MarkView.Note switch
    {
        DownloadMarkNote.Cost => _res.GetString("DownloadMarkCostNote"),
        DownloadMarkNote.Risk => _res.GetString("DownloadMarkRiskNote"),
        DownloadMarkNote.Policy => _res.GetString("DownloadMarkPolicyNote"),
        _ => string.Empty,
    };

    private void RefreshDownloadMark()
    {
        int generation = ++_downloadMarkGeneration;
        _anyArchiveMarked = false;
        ApplyDownloadMark = true;
        RaiseDownloadMark();
        string[] archives = IsBrowsingArchive
            ? (BrowsedArchivePath is { } browsed ? [browsed] : [])
            : [.. _listActions.ExtractablePaths];
        if (archives.Length > 0)
            _ = ReadMarksAsync(archives, generation);
    }

    private async Task ReadMarksAsync(string[] archives, int generation)
    {
        bool marked = await Task.Run(() => archives.Any(ArchiveDownloadMark.IsPresent));
        if (generation != _downloadMarkGeneration)
            return;
        _anyArchiveMarked = marked;
        RaiseDownloadMark();
    }

    private void RaiseDownloadMark()
    {
        OnPropertyChanged(nameof(DownloadMarkVisibility));
        OnPropertyChanged(nameof(DownloadMarkChecked));
        OnPropertyChanged(nameof(DownloadMarkCanChange));
        OnPropertyChanged(nameof(DownloadMarkNoteText));
    }

    // T-F05: collapses while the archive browser is open.
    public Visibility NewArchiveCardVisibility =>
        IsBrowsingArchive ? Visibility.Collapsed : Visibility.Visible;

    // T-F199 board 8: an archives-only list collapses the card; the user can open it again.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewArchiveSummaryVisibility))]
    private bool _isNewArchiveCardExpanded = true;

    public Visibility NewArchiveSummaryVisibility =>
        IsNewArchiveCardExpanded ? Visibility.Collapsed : Visibility.Visible;

    // The options the collapsed card still applies if the user presses Compress.
    public string NewArchiveSummary
    {
        get
        {
            string summary = string.Join(" · ",
                new[] { CreateModeText.FormatName(SelectedContainerFormat) }
                    .Concat(CreateModeText.SummaryKeys(SelectedContainerFormat, SelectedCompressionLevel, EncryptWithPassword).Select(_res.GetString)));
            return _listActions.ArchivesOnly ? summary + " — " + _res.GetString("NewArchiveCollapsedReason") : summary;
        }
    }

    public string ArchiveNamePlaceholder => string.Format(System.Globalization.CultureInfo.CurrentCulture,
        _res.GetString(SelectedArchiveMode == ArchiveMode.SeparateArchives ? "ArchiveNamePerItemPlaceholder" : "ArchiveNameAutoPlaceholder"),
        CreateModeText.AutoName([.. FileItems.Select(x => x.FullPath)], SelectedArchiveMode, SelectedContainerFormat));

    public string DestinationLabel =>
        _res.GetString(CreateModeText.DestinationLabelKey(IsExtractAccent ? PrimaryAction.Extract : PrimaryAction.Compress));

    public string DeleteAfterLabel =>
        _res.GetString(CreateModeText.DeleteAfterKey(_listActions.CanCompress, _listActions.CanExtract, IsBrowsingArchive));

    // The empty list dims the options: nothing they apply to yet (board 3).
    public double OptionsOpacity => FileItems.Count == 0 && !IsBrowsingArchive ? 0.55 : 1.0;

    // T-F211/T-F199 step 7: the last operation's result, kept until the next action (an operation
    // starting, the list changing, a selection, opening or closing an archive). Null: no line.
    private string? _outcomeText;
    private OutcomeLine? _outcomeLine;
    private ArchiveResult? _outcomeResult;
    private string _outcomeOperation = string.Empty;

    private FooterLineKind FooterKind =>
        FooterLine.Pick(IsBusy, _outcomeText is not null, IsBrowsingArchive, SelectedBrowserEntries.Count, FileItems.Count > 0);

    public string FooterText => FooterKind switch
    {
        FooterLineKind.Outcome => _outcomeText!,
        FooterLineKind.Selection => string.Format(System.Globalization.CultureInfo.CurrentCulture,
            _res.GetString("BrowseSelectionLine"), SelectedBrowserEntries.Count, CurrentFolderEntries.Count),
        FooterLineKind.Preview => _listActions.Accent == PrimaryAction.Extract
            ? _res.GetString("OutcomeWillExtract").Replace("{0}", _listActions.ExtractablePaths.Count.ToString())
            : _res.GetString("OutcomeWillArchive").Replace("{0}", FileItems.Count.ToString()),
        _ => string.Empty,
    };

    public Visibility FooterTextVisibility =>
        FooterKind == FooterLineKind.None ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ShowOutcomeInFolderVisibility =>
        FooterKind == FooterLineKind.Outcome && _outcomeLine?.ExplorerArguments is not null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OutcomeDetailsVisibility =>
        FooterKind == FooterLineKind.Outcome && _outcomeLine?.HasDetails == true ? Visibility.Visible : Visibility.Collapsed;

    // "Ready" under a result line adds nothing.
    public Visibility StatusLineVisibility =>
        FooterKind == FooterLineKind.Outcome ? Visibility.Collapsed : Visibility.Visible;

    private void RaiseFooter()
    {
        OnPropertyChanged(nameof(FooterText));
        OnPropertyChanged(nameof(FooterTextVisibility));
        OnPropertyChanged(nameof(ShowOutcomeInFolderVisibility));
        OnPropertyChanged(nameof(OutcomeDetailsVisibility));
        OnPropertyChanged(nameof(StatusLineVisibility));
    }

    private void SetOutcome(string text, OutcomeLine? line, ArchiveResult? result, string operation)
    {
        _outcomeText = text;
        _outcomeLine = line;
        _outcomeResult = result;
        _outcomeOperation = operation;
        RaiseFooter();
    }

    private void ClearOutcome()
    {
        if (_outcomeText is null)
            return;
        _outcomeText = null;
        _outcomeLine = null;
        _outcomeResult = null;
        RaiseFooter();
    }

    // T-F280: the summary dialog shows warnings; a file preview and a drill into a nested archive
    // have no summary, so there the log is the only place a warning lands.
    private void LogWarnings(ArchiveResult result)
    {
        foreach (ArchiveWarning warning in result.Warnings)
            _logService.Warn($"{warning.SourcePath}: {warning.Message}");
    }

    private static string RenderOutcome(OutcomeLine line) => string.Format(System.Globalization.CultureInfo.CurrentCulture,
        _res.GetString(line.TextKey), [.. line.TextArgs.Cast<object>()]);

    // Absolute path, never a bare "explorer.exe" (S4036, T-F136).
    private static readonly string ExplorerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    [RelayCommand]
    private void ShowOutcomeInFolder()
    {
        string? arguments = _outcomeLine?.ExplorerArguments;
        if (arguments is null)
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExplorerPath, arguments));
        }
        catch (Exception ex)
        {
            _logService.Warn($"Show in folder failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private Task ShowOutcomeDetailsAsync() => _outcomeResult is null
        ? Task.CompletedTask
        : _dialogService.ShowOperationSummaryAsync(_outcomeOperation, _outcomeResult);

    // T-F82/T-F199: the accent sits on the action that fits the list, and the primary button is
    // always the rightmost one (footer columns 3 and 4).
    public Style? ArchiveButtonStyle =>
        _listActions.Accent == PrimaryAction.Extract ? null : WinRT.CastExtensions.As<Style>(Application.Current.Resources["AccentButtonStyle"]);

    public Style? ExtractButtonStyle =>
        _listActions.Accent == PrimaryAction.Extract ? WinRT.CastExtensions.As<Style>(Application.Current.Resources["AccentButtonStyle"]) : null;

    public int ArchiveButtonColumn => _listActions.Accent == PrimaryAction.Extract ? 3 : 4;

    public int ExtractButtonColumn => _listActions.Accent == PrimaryAction.Extract ? 4 : 3;

    // T-F212: why Extract is off; null (no tooltip) when it is on or the list is empty.
    public string? ExtractUnavailableHint =>
        _listActions.ExtractUnavailable ? _res.GetString("ExtractUnavailableHint") : null;

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
    [NotifyPropertyChangedFor(nameof(NewArchiveSummary))]
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
    [NotifyPropertyChangedFor(nameof(ArchiveButtonText))]
    [NotifyPropertyChangedFor(nameof(IsCompressionLevelEnabled))]
    [NotifyPropertyChangedFor(nameof(NewArchiveSummary))]
    [NotifyPropertyChangedFor(nameof(ArchiveNamePlaceholder))]
    [NotifyPropertyChangedFor(nameof(EncryptCheckVisibility))]
    [NotifyPropertyChangedFor(nameof(EncryptZipOnlyVisibility))]
    [NotifyPropertyChangedFor(nameof(EncryptionPanelVisibility))]
    private ArchiveContainerFormat _selectedContainerFormat = ArchiveContainerFormat.Zip;

    partial void OnSelectedContainerFormatChanged(ArchiveContainerFormat value) => ClearEncryptionPassword();

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

    // T-F193: only the ZIP writer can encrypt. The checkbox keeps its checked state when a tar
    // format is picked (it's merely hidden), so ArchiveAsync checks the format again at use.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewArchiveSummary))]
    [NotifyPropertyChangedFor(nameof(EncryptionPanelVisibility))]
    private bool _encryptWithPassword = false;

    partial void OnEncryptWithPasswordChanged(bool value) => ClearEncryptionPassword();

    // T-F199 step 5: the password is typed inline under the checkbox (it replaced a modal that
    // validated only after OK). The PasswordBoxes live in the view; it forwards every change here
    // and empties the boxes on EncryptionPasswordCleared. Nothing is stored or logged, and the
    // text is dropped after every operation, on untick, on a format change and on window close.
    public event EventHandler? EncryptionPasswordCleared;

    public Visibility EncryptionPanelVisibility =>
        InlinePasswordState.Applies(EncryptWithPassword, SelectedContainerFormat) ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EncryptionPasswordRevealMode))]
    private bool _showEncryptionPassword;

    public Microsoft.UI.Xaml.Controls.PasswordRevealMode EncryptionPasswordRevealMode => ShowEncryptionPassword
        ? Microsoft.UI.Xaml.Controls.PasswordRevealMode.Visible
        : Microsoft.UI.Xaml.Controls.PasswordRevealMode.Hidden;

    public static string EncryptPasswordNote => _res.GetString("EncryptPasswordDialogMessage");
    public static string EncryptPasswordPlaceholder => _res.GetString("EncryptPasswordPlaceholder");

    // T-F198 items 2 and 6: names for controls that show only a glyph (the Cancel label starts with one).
    public static string CancelButtonName => _res.GetString("PasswordDialogCancelButton");
    public static string BrowseUpButtonName => _res.GetString("BrowseUpButtonName");
    public static string DestinationUpButtonName => _res.GetString("DestinationUpButtonName");
    public static string DestinationBrowseButtonName => _res.GetString("DestinationBrowseButtonName");
    public static string EncryptedEntryIconName => _res.GetString("BrowseEncryptedBadgeUnknown");
    public static string DragAddCaption => _res.GetString("DragAddCaption");
    public static string EncryptPasswordConfirmPlaceholder => _res.GetString("EncryptPasswordConfirmPlaceholder");
    public static string EncryptPasswordRuleHint =>
        _res.GetString("EncryptPasswordRuleHint").Replace("{0}", EncryptionPasswordRule.MaxLength.ToString());

    public string EncryptionPasswordMessage => _encryptionPassword.MessageKey is { } key
        ? _res.GetString(key).Replace("{0}", EncryptionPasswordRule.MaxLength.ToString())
        : string.Empty;

    public Visibility EncryptionPasswordMessageVisibility =>
        _encryptionPassword.MessageKey is null ? Visibility.Collapsed : Visibility.Visible;

    // Returns what the box should show: a refused character never gets in.
    public string SetEncryptionPassword(string value)
    {
        string kept = _encryptionPassword.SetPassword(value);
        OnEncryptionPasswordEdited();
        return kept;
    }

    public string SetEncryptionConfirmation(string value)
    {
        string kept = _encryptionPassword.SetConfirmation(value);
        OnEncryptionPasswordEdited();
        return kept;
    }

    public void ClearEncryptionPassword()
    {
        _encryptionPassword.Clear();
        ShowEncryptionPassword = false;
        EncryptionPasswordCleared?.Invoke(this, EventArgs.Empty);
        OnEncryptionPasswordEdited();
    }

    private void OnEncryptionPasswordEdited()
    {
        OnPropertyChanged(nameof(EncryptionPasswordMessage));
        OnPropertyChanged(nameof(EncryptionPasswordMessageVisibility));
        ArchiveCommand.NotifyCanExecuteChanged();
    }

    // T-F198 item 5: a tar format shows why there is no password instead of a disabled, still
    // ticked checkbox.
    public Visibility EncryptCheckVisibility =>
        SelectedContainerFormat == ArchiveContainerFormat.Zip ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EncryptZipOnlyVisibility =>
        SelectedContainerFormat == ArchiveContainerFormat.Zip ? Visibility.Collapsed : Visibility.Visible;

    // T-F51: DisableTarExtraction also hides the 6 tar-family Format ComboBoxItems — GroupPolicy
    // is loaded once at process startup and never changes mid-session, so this is a fixed value
    // for the lifetime of the ViewModel, not an [ObservableProperty].
    public Visibility TarFormatVisibility =>
        _policy.DisableTarExtraction ? Visibility.Collapsed : Visibility.Visible;

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
    [NotifyPropertyChangedFor(nameof(NewArchiveCardVisibility))]
    [NotifyPropertyChangedFor(nameof(DestinationLabel))]
    [NotifyPropertyChangedFor(nameof(DeleteAfterLabel))]
    [NotifyPropertyChangedFor(nameof(OptionsOpacity))]
    [NotifyCanExecuteChangedFor(nameof(ExtractAllFromBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractSelectedFromBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanArchiveFromBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseArchiveCommand))]
    private bool _isBrowsingArchive = false;

    partial void OnIsBrowsingArchiveChanged(bool value)
    {
        RaiseBrowseLocationChanged();
        RaiseFooter();
        RefreshDownloadMark();
    }

    partial void OnBrowseScopeChanged(ArchiveBrowseScope value)
    {
        RaiseBrowseLocationChanged();
        RaiseDownloadMark();
    }

    partial void OnBrowsedArchivePathChanged(string? value) => RefreshDownloadMark();

    private BrowseLocationState Location => BrowseLocationState.For(
        IsBrowsingArchive && BrowseScope == ArchiveBrowseScope.Archive, _browseStack.Count > 0, _browsedIsZip);

    public Visibility BrowseExtractActionsVisibility =>
        IsBrowsingArchive && Location.ShowsExtractActions ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OptionsVisibility =>
        !IsBrowsingArchive || Location.ShowsOptions ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DeleteAfterVisibility =>
        !IsBrowsingArchive || Location.OffersDeleteAfter ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TestArchiveVisibility =>
        IsBrowsingArchive && Location.ShowsTest ? Visibility.Visible : Visibility.Collapsed;

    private bool ShowsEncryption => IsBrowsingArchive && BrowseScope == ArchiveBrowseScope.Archive
        && _browseEncryption is { IsEncrypted: true };

    public Visibility EncryptionBadgeVisibility => ShowsEncryption ? Visibility.Visible : Visibility.Collapsed;

    public string EncryptionBadgeText =>
        _browseEncryption?.BadgeName ?? _res.GetString("BrowseEncryptedBadgeUnknown");

    // T-F199 board 5/6: one line under the breadcrumb — the encryption notes inside an encrypted
    // archive, where the user is outside one.
    public bool IsBrowseInfoOpen => IsBrowsingArchive && (Location.ShowsOutsideInfo || ShowsEncryption);

    public Microsoft.UI.Xaml.Controls.InfoBarSeverity BrowseInfoSeverity =>
        ShowsEncryption && _browseEncryption!.HasZipCrypto ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;

    public string BrowseInfoText
    {
        get
        {
            if (!IsBrowsingArchive) return string.Empty;
            if (Location.ShowsOutsideInfo) return _res.GetString("BrowseOutsideInfo");
            if (!ShowsEncryption) return string.Empty;
            EncryptionSummary summary = _browseEncryption!;
            string count = string.Format(System.Globalization.CultureInfo.CurrentCulture, _res.GetString("BrowseEncryptedCount"),
                summary.BadgeName ?? _res.GetString("BrowseEncryptedUnknownMethod"), summary.EncryptedFiles, summary.TotalFiles);
            return string.Join(" ", new[] { count }.Concat(summary.NoteKeys.Select(_res.GetString)));
        }
    }

    private void SetBrowseLevel(EncryptionSummary? encryption, bool isZip)
    {
        _browseEncryption = encryption;
        _browsedIsZip = isZip;
        RaiseBrowseLocationChanged();
    }

    private void RaiseBrowseLocationChanged()
    {
        OnPropertyChanged(nameof(BrowseExtractActionsVisibility));
        OnPropertyChanged(nameof(OptionsVisibility));
        OnPropertyChanged(nameof(DeleteAfterVisibility));
        OnPropertyChanged(nameof(TestArchiveVisibility));
        OnPropertyChanged(nameof(EncryptionBadgeVisibility));
        OnPropertyChanged(nameof(EncryptionBadgeText));
        OnPropertyChanged(nameof(IsBrowseInfoOpen));
        OnPropertyChanged(nameof(BrowseInfoSeverity));
        OnPropertyChanged(nameof(BrowseInfoText));
        TestBrowsedArchiveCommand.NotifyCanExecuteChanged();
    }

    public Visibility IsPendingListVisibility =>
        IsBrowsingArchive ? Visibility.Collapsed : Visibility.Visible;

    public Visibility IsBrowsingArchiveVisibility =>
        IsBrowsingArchive ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtractAllFromBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExtractSelectedFromBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanArchiveFromBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestBrowsedArchiveCommand))]
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

    // Null until the first header click: the list keeps the order items were added in.
    private string? _sortColumn;
    private bool _sortAscending = true;

    // T-F220 item 4: the header arrow of the sorted column.
    public string NameSortGlyph => SortIndicator.Glyph("Name", _sortColumn, _sortAscending);
    public string TypeSortGlyph => SortIndicator.Glyph("Type", _sortColumn, _sortAscending);
    public string SizeSortGlyph => SortIndicator.Glyph("Size", _sortColumn, _sortAscending);
    public string CrcSortGlyph => SortIndicator.Glyph("Crc", _sortColumn, _sortAscending);
    public string ModifiedSortGlyph => SortIndicator.Glyph("Modified", _sortColumn, _sortAscending);

    public MainViewModel(
        IArchiveCreationRouter archiveCreationRouter,
        IExtractionRouter extractionRouter,
        IArchiveListingRouter archiveListingRouter,
        IAntivirusScanService antivirusScanService,
        IDialogService dialogService,
        ILogService logService,
        GroupPolicyOptions groupPolicyOptions,
        SourceRecycler sourceRecycler,
        TarCapabilities tarCapabilities)
    {
        _tarCapabilities = tarCapabilities;
        _sourceRecycler = sourceRecycler;
        _archiveCreationRouter = archiveCreationRouter;
        _extractionRouter = extractionRouter;
        _archiveListingRouter = archiveListingRouter;
        _antivirusScanService = antivirusScanService;
        _dialogService = dialogService;
        _logService = logService;
        _policy = groupPolicyOptions;
        _browseWork.Changed += CloseArchiveCommand.NotifyCanExecuteChanged;

        // T-F51: defensive only — nothing in this ViewModel currently sets
        // SelectedContainerFormat away from its Zip default except user interaction with the
        // (now-hidden) tar ComboBoxItems, so this should be unreachable today. Kept in case a
        // future caller (e.g. protocol activation) ever sets a tar format directly.
        if (_policy.DisableTarExtraction && SelectedContainerFormat != ArchiveContainerFormat.Zip)
            SelectedContainerFormat = ArchiveContainerFormat.Zip;

        _fileItems.CollectionChanged += (_, _) =>
        {
            ArchiveCommand.NotifyCanExecuteChanged();
            UpdateDefaultDestination();
            OnPropertyChanged(nameof(IsFileListEmpty));
            OnPropertyChanged(nameof(IsFileListEmptyVisibility));
            bool wasArchivesOnly = _listActions.ArchivesOnly;
            _listActions = PrimaryActionPolicy.Evaluate(
                [.. FileItems.Select(x => (x.FullPath, x.IsFolder))],
                path => ArchiveFormatPolicy.CanOpenByExtension(path, _tarCapabilities, _policy));
            if (_listActions.ArchivesOnly != wasArchivesOnly)
                IsNewArchiveCardExpanded = !_listActions.ArchivesOnly;
            ExtractCommand.NotifyCanExecuteChanged();
            ClearOutcome();
            RaiseFooter();
            OnPropertyChanged(nameof(ArchiveButtonStyle));
            OnPropertyChanged(nameof(ExtractButtonStyle));
            OnPropertyChanged(nameof(ArchiveButtonColumn));
            OnPropertyChanged(nameof(ExtractButtonColumn));
            OnPropertyChanged(nameof(ExtractUnavailableHint));
            OnPropertyChanged(nameof(ArchiveNamePlaceholder));
            OnPropertyChanged(nameof(DestinationLabel));
            OnPropertyChanged(nameof(DeleteAfterLabel));
            OnPropertyChanged(nameof(OptionsOpacity));
            OnPropertyChanged(nameof(NewArchiveSummary));
            RefreshDownloadMark();
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
        OnPropertyChanged(nameof(NameSortGlyph));
        OnPropertyChanged(nameof(TypeSortGlyph));
        OnPropertyChanged(nameof(SizeSortGlyph));
        OnPropertyChanged(nameof(CrcSortGlyph));
        OnPropertyChanged(nameof(ModifiedSortGlyph));
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
        string? folder = await _dialogService.PickDestinationFolderAsync();
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
        string? parent = Path.GetDirectoryName(DestinationPath);
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
        string? outcomeText = null;
        OutcomeLine? outcomeLine = null;
        ArchiveResult? outcomeResult = null;
        // T-F199 step 5: read once on the UI thread; Core asks from a worker thread, once per
        // archive in SeparateArchives mode. CanArchive already refused an unusable password.
        string? encryptionPassword = InlinePasswordState.Applies(EncryptWithPassword, SelectedContainerFormat)
            ? _encryptionPassword.Password
            : null;
        try
        {
            var options = new ArchiveOptions
            {
                ResolvePasswordAsync = encryptionPassword is null
                    ? null
                    : _ => Task.FromResult(new global::Archiver.Core.Models.PasswordDecision { Password = encryptionPassword }),
                SourcePaths = [.. FileItems.Select(x => x.FullPath)],
                DestinationFolder = DestinationPath,
                ArchiveName = string.IsNullOrWhiteSpace(ArchiveName) ? null : ArchiveName.Trim(),
                Mode = SelectedArchiveMode,
                OnConflict = OnConflict,
                OpenDestinationFolder = OpenDestinationFolder,
                CompressionLevel = SelectedCompressionLevel,
                Format = SelectedContainerFormat,
                ResolveConflictAsync = conflict => _dialogService.ShowConflictDialogAsync(conflict, () => _cts?.Cancel()),
            };

            // T-F236: the rows measured themselves when they were added; this used to walk every
            // folder again, on the UI thread.
            FileItem[] items = [.. FileItems];
            await Task.WhenAll(items.Select(i => i.TotalsReady)).WaitAsync(_cts.Token);
            long totalBytes = items.Sum(i => Math.Max(i.SizeBytes, 0));
            int fileCount = items.Sum(i => i.FileCount);

            string sizeStr = DisplayText.FormatSize(totalBytes);

            _operationStatusPrefix = string.Format(System.Globalization.CultureInfo.CurrentCulture, _res.GetString("StatusArchivingCount"), fileCount, sizeStr);
            StatusMessage = _operationStatusPrefix;
            _operationStopwatch = System.Diagnostics.Stopwatch.StartNew();
            _speedSampler = new ProgressSpeedSampler();
            (_estimateStart, _estimateStartPercent, _checkingArchive) = (TimeSpan.Zero, 0, false);

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

            ArchiveResult result = await _archiveCreationRouter.ArchiveAsync(options, progress, _cts.Token);
            _operationStopwatch?.Stop();
            outcomeLine = OutcomeLine.From(result, _operationStopwatch?.Elapsed ?? TimeSpan.Zero, extract: false, DestinationPath);
            outcomeResult = result;
            outcomeText = RenderOutcome(outcomeLine);
            StatusMessage = outcomeText;
            _logService.Info($"Archive completed — {result.CreatedFiles.Count} file(s) → {DestinationPath}");
            foreach (SkippedFile skipped in result.SkippedFiles)
                _logService.Warn($"Skipped {skipped.Path} — {skipped.Reason}");
            foreach (ArchiveError error in result.Errors)
                _logService.Error($"{error.SourcePath} — {error.Message}");
            await _dialogService.ShowOperationSummaryAsync("Archive", result);
            // T-F260/T-F229: only sources Core reports as fully processed, and only after the
            // summary, so the user sees what was skipped before anything is deleted. A cancel
            // throws and never reaches this line.
            if (DeleteAfterOperation)
                await RunCleanupAsync(result.FullyProcessedSources);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            _operationStopwatch?.Stop();
            outcomeText = _res.GetString("StatusCancelled");
            StatusMessage = outcomeText;
        }
        catch (Exception ex)
        {
            _operationStopwatch?.Stop();
            outcomeText = _res.GetString("DialogErrorTitle");
            StatusMessage = outcomeText;
            _logService.Error("Unexpected error during operation", ex);
            await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"), ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsProgressIndeterminate = false;
            ClearEncryptionPassword();
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
        if (outcomeText is not null)
            SetOutcome(outcomeText, outcomeLine, outcomeResult, "Archive");
    }

    [RelayCommand(CanExecute = nameof(CanExtract))]
    private Task ExtractAsync() =>
        RunExtractAsync(_listActions.ExtractablePaths, selectedEntryPaths: null);

    // T-F05: shared by the whole-archive Extract button and the archive browser's Extract
    // Selected/Extract All/double-click-a-file commands below — the entire IsBusy/progress/
    // stopwatch/bomb-confirm-callback/summary-dialog/cleanup sequence stays identical for both;
    // only which archive(s) and which entry subset (if any) get passed to ExtractOptions differ.
    // allowDeleteAfter: false for a nested archive in the browser — a temp copy (T-F199 step 6).
    private async Task RunExtractAsync(IReadOnlyList<string> archivePaths, IReadOnlyList<string>? selectedEntryPaths, string? destinationOverride = null, bool fromBrowser = false, bool allowDeleteAfter = true)
    {
        _cts = new CancellationTokenSource();
        bool closeBrowser = false;
        _lastOperation = "extract";
        IsBusy = true;
        CancelCommand.NotifyCanExecuteChanged();
        Progress = 0;
        bool wasCancelled = false;
        string? outcomeText = null;
        OutcomeLine? outcomeLine = null;
        ArchiveResult? outcomeResult = null;
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
                ConfirmCompressionBombExtraction = _dialogService.ShowCompressionBombConfirmAsync,
                ResolveConflictAsync = conflict => _dialogService.ShowConflictDialogAsync(conflict, () => _cts?.Cancel()),
                ResolvePasswordAsync = fromBrowser
                    ? BrowsePasswordResolver(archivePaths[0])
                    : info => _dialogService.ShowPasswordPromptAsync(info, archivePaths.Count > 1),
                SelectedEntryPaths = selectedEntryPaths,
                // T-F360: the single file the unsafe-type warning extracts is about to be opened.
                ApplyDownloadMark = destinationOverride is not null || MarkView.ApplyMark,
            };

            _operationStatusPrefix = string.Format(System.Globalization.CultureInfo.CurrentCulture, _res.GetString("StatusExtractingCount"), options.ArchivePaths.Count);
            StatusMessage = _operationStatusPrefix;
            _operationStopwatch = System.Diagnostics.Stopwatch.StartNew();
            _speedSampler = new ProgressSpeedSampler();
            (_estimateStart, _estimateStartPercent, _checkingArchive) = (TimeSpan.Zero, 0, false);

            var progress = new Progress<ProgressReport>(r =>
            {
                Progress = r.Percent;
                UpdateOperationStatus(r);
            });

            ArchiveResult result = await _extractionRouter.ExtractAsync(options, progress, _cts.Token);
            if (fromBrowser)
                _browsePasswords.Complete(archivePaths[0], result);
            _operationStopwatch?.Stop();
            outcomeLine = OutcomeLine.From(result, _operationStopwatch?.Elapsed ?? TimeSpan.Zero, extract: true, options.DestinationFolder);
            outcomeResult = result;
            outcomeText = RenderOutcome(outcomeLine);
            StatusMessage = outcomeText;
            _logService.Info($"Extract completed — {result.CreatedFiles.Count} file(s) → {DestinationPath}");
            foreach (SkippedFile skipped in result.SkippedFiles)
                _logService.Warn($"Skipped {skipped.Path} — {skipped.Reason}");
            foreach (ArchiveError error in result.Errors)
                _logService.Error($"{error.SourcePath} — {error.Message}");
            LogWarnings(result);
            await _dialogService.ShowOperationSummaryAsync("Extract", result);
            // T-F260/T-F229/T-F265: see ArchiveAsync — a subset extraction is never deletable.
            if (DeleteAfterOperation && allowDeleteAfter)
            {
                await RunCleanupAsync(result.FullyProcessedSources);
                // The browsed archive went to the Recycle Bin: nothing left to browse.
                closeBrowser = fromBrowser && !File.Exists(archivePaths[0]);
            }
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            _operationStopwatch?.Stop();
            outcomeText = _res.GetString("StatusCancelled");
            StatusMessage = outcomeText;
        }
        catch (Exception ex)
        {
            _operationStopwatch?.Stop();
            outcomeText = _res.GetString("DialogErrorTitle");
            StatusMessage = outcomeText;
            _logService.Error("Unexpected error during operation", ex);
            await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"), ex.Message);
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
        if (closeBrowser)
            CloseArchiveCore();
        // Last: closing the browser above must not take the result line with it.
        if (outcomeText is not null)
            SetOutcome(outcomeText, outcomeLine, outcomeResult, "Extract");
    }

    [RelayCommand(CanExecute = nameof(IsOperationRunning))]
    private void Cancel() => _cts?.Cancel();

    // ── Archive Browser (T-F05) ──────────────────────────────────────────────────

    public async Task EnterBrowseModeAsync(string archivePath)
    {
        using IDisposable work = _browseWork.Begin();
        BrowseListFailureStep onListFailure = BrowseNavigation.DecideListFailure(IsBrowsingArchive, BrowseScope);
        string priorFolderPath = CurrentFolderPath;
        ClearOutcome();
        SetBrowseLevel(null, isZip: false);
        // "Delete after" means sources in create mode and the archive here: a tick never carries
        // across the switch (T-F199 step 6).
        DeleteAfterOperation = false;
        IsBrowsingArchive = true;
        BrowseScope = ArchiveBrowseScope.Archive;
        BrowsedArchivePath = archivePath;
        CurrentFolderPath = string.Empty;
        SelectedBrowserEntries = [];
        ResetNestedBrowseStack();
        _browsePasswords.Clear();

        // T-F106: this is awaited un-awaited (fire-and-forget) from App.xaml.cs's deferred
        // activation path — a thrown exception there would otherwise leave IsBrowsingArchive
        // stuck true with no archive index and no visible error (found via code-review advisor
        // pass). ListArchiveWithProgressAsync catching internally, not just at the activation
        // call site, fixes it for every caller, matching the same reset+dialog recovery already
        // used for result.Success==false below.
        ArchiveListResult? result = await ListArchiveWithProgressAsync(archivePath);
        if (result is null)
        {
            LeaveFailedListing(onListFailure, priorFolderPath);
            return;
        }

        if (!result.Success)
        {
            LeaveFailedListing(onListFailure, priorFolderPath);
            await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"), CoreMessageText.Of(result.ErrorText, result.ErrorMessage ?? "Failed to read archive."));
            return;
        }

        // Bug found 2026-07-17: entering browse mode via file activation (T-F100) or by
        // double-clicking a real archive found while browsing real folders (T-F107) never goes
        // through AddPaths, so FileItems stays empty and UpdateDefaultDestination() (only wired
        // to FileItems.CollectionChanged) never fires — DestinationPath then silently stays at
        // its Desktop default regardless of where the archive actually lives. Only apply this
        // when FileItems is empty — the pending-list double-click entry point (T-F05's original
        // flow) already got a correct destination from UpdateDefaultDestination() when the
        // archive was added, and a user may have since picked a different one deliberately.
        // Set only once the archive lists (T-F319): a failed open changes nothing.
        if (FileItems.Count == 0)
            DestinationPath = Path.GetDirectoryName(archivePath) ?? DestinationPath;

        _archiveIndex = ArchiveTreeIndex.Build(result.Entries);
        SetBrowseLevel(EncryptionSummary.Of(result.Entries), IsZipOnDisk(archivePath));
        RefreshCurrentFolder();
    }

    // T-F319: an archive opened from a browsed real folder leaves the user in that folder.
    private void LeaveFailedListing(BrowseListFailureStep step, string priorFolderPath)
    {
        BrowsedArchivePath = null;
        if (step == BrowseListFailureStep.BackToRealFolder)
        {
            BrowseScope = ArchiveBrowseScope.RealFileSystem;
            CurrentFolderPath = priorFolderPath;
            RefreshCurrentFolder();
            return;
        }

        IsBrowsingArchive = false;
    }

    private static bool IsZipOnDisk(string archivePath) => ArchiveFormatDetector.Detect(archivePath) == ArchiveFormat.Zip;

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
            await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"), ex.Message);
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
            NestedBrowseLevel level = _browseStack.Pop();
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
        using IDisposable work = _browseWork.Begin();

        if (NestedArchivePolicy.ExceedsMaxDepth(_browseStack.Count))
        {
            await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"), _res.GetString("NestedArchiveDepthLimitReached"));
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
            ResolvePasswordAsync = BrowsePasswordResolver(BrowsedArchivePath),
        };

        StatusMessage = _res.GetString("StatusOpening");
        ArchiveResult result;
        try
        {
            result = await _extractionRouter.ExtractAsync(options);
            _browsePasswords.Complete(options.ArchivePaths[0], result);
            LogWarnings(result);
        }
        finally
        {
            StatusMessage = _res.GetString("StatusReady");
        }

        if (!result.Success || result.CreatedFiles.Count == 0)
        {
            NestedArchiveCache.DeleteScope(scopeDir);
            await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"), _res.GetString("StatusIssues"));
            return;
        }

        // The entry may itself not really be an archive despite its extension (ArchiveFormatDetector
        // couldn't check this before extraction — its magic-byte sniff needs a real file on disk).
        // Confirm now, mirroring EnterBrowseModeAsync's own "detect what you actually got" posture.
        string? extractedPath = BrowserEntryRouting.ResolveInScope(scopeDir, entry.FullPath);
        if (extractedPath is null || ArchiveFormatDetector.Detect(extractedPath) == ArchiveFormat.Unknown)
        {
            NestedArchiveCache.DeleteScope(scopeDir);
            await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"), _res.GetString("StatusIssues"));
            return;
        }

        ArchiveListResult? listResult = await ListArchiveWithProgressAsync(extractedPath);
        if (listResult is null || !listResult.Success)
        {
            NestedArchiveCache.DeleteScope(scopeDir);
            if (listResult is not null)
                await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"), CoreMessageText.Of(listResult.ErrorText, listResult.ErrorMessage ?? "Failed to read archive."));
            return;
        }

        _browseStack.Push(new NestedBrowseLevel(
            BrowsedArchivePath,
            CurrentFolderPath,
            _currentLevelDisplayName,
            _archiveIndex,
            new List<string>(_nestedBreadcrumbAncestry),
            _currentNestedScopeDir,
            _browseEncryption,
            _browsedIsZip));

        _nestedBreadcrumbAncestry.Add(_currentLevelDisplayName ?? Path.GetFileName(BrowsedArchivePath ?? string.Empty));
        if (CurrentFolderPath.Length > 0)
            _nestedBreadcrumbAncestry.AddRange(CurrentFolderPath.Split('/'));

        _currentLevelDisplayName = entry.Name;
        _currentNestedScopeDir = scopeDir;
        BrowsedArchivePath = extractedPath;
        CurrentFolderPath = string.Empty;
        _archiveIndex = ArchiveTreeIndex.Build(listResult.Entries);
        SetBrowseLevel(EncryptionSummary.Of(listResult.Entries), IsZipOnDisk(extractedPath));
        RefreshCurrentFolder();
    }

    private void RefreshCurrentFolder()
    {
        // T-F05/T-F107: a childless archive folder (explicit empty directory entry) is a node in
        // its parent's child list but has no children of its own — TryGetChildren is false for it,
        // hence the empty fallback.
        IReadOnlyList<ArchiveEntryViewModel> entries = BrowseScope switch
        {
            ArchiveBrowseScope.RealFileSystem => FileSystemBrowser.ListFolder(CurrentFolderPath),
            ArchiveBrowseScope.ThisPc => FileSystemBrowser.ListDrives(),
            _ => _archiveIndex.TryGetChildren(CurrentFolderPath, out IReadOnlyList<ArchiveEntryViewModel> list) ? list : [],
        };

        // T-F98/T-F110: only a nested-archive row inside the currently browsed archive can ever
        // be blocked by the depth limit (NavigateIntoNestedArchiveAsync) — real-filesystem/drive
        // browsing always opens an archive fresh (depth 0), so this never applies there.
        if (BrowseScope == ArchiveBrowseScope.Archive && NestedArchivePolicy.ExceedsMaxDepth(_browseStack.Count))
        {
            entries = entries
                .Select(e => !e.IsFolder && ArchiveFormatDetector.IsRecognizedArchiveExtension(e.Name)
                    ? e with { NestedDepthLimitReached = true }
                    : e)
                .ToList();
        }

        CurrentFolderEntries = new ObservableCollection<ArchiveEntryViewModel>(entries);
        SelectedBrowserEntries = [];
        RebuildBreadcrumb();
    }

    // T-F220 item 5: a nested level's BrowsedArchivePath is its temp copy; shown text names the
    // chain instead (the breadcrumb's own segments up to this level's root).
    private string ForDisplay(string text) => _currentNestedScopeDir is null
        ? text
        : NestedDisplayPath.Map(text, BrowsedArchivePath,
            NestedDisplayPath.Chain([.. _nestedBreadcrumbAncestry, _currentLevelDisplayName ?? Path.GetFileName(BrowsedArchivePath ?? string.Empty)]));

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

    // T-F05/T-F242: double-clicking a pending-list row opens a real archive in the browser. The
    // magic-byte probe runs off the UI thread.
    public async Task OpenPendingRowAsync(FileItem item)
    {
        bool isArchive = !IsBusy && !item.IsFolder && await Task.Run(() => IsArchiveOnDisk(item.FullPath));
        RowOpenAction action = BrowserEntryRouting.DecidePendingRow(IsBusy, item.IsFolder, () => isArchive);
        if (action == RowOpenAction.OpenArchive)
            await EnterBrowseModeAsync(item.FullPath);
    }

    // T-F242: the Archive Browser's double-click routing (T-F05 folders, T-F107 outside an
    // archive, T-F98 nested archives, T-F97 preview, T-F109 warned extract), moved out of
    // code-behind into BrowserEntryRouting.
    public async Task OpenBrowserRowAsync(ArchiveEntryViewModel entry)
    {
        using IDisposable work = _browseWork.Begin();
        bool insideArchive = BrowseScope == ArchiveBrowseScope.Archive;
        bool isArchive = !IsBusy && !entry.IsFolder && !insideArchive
            && await Task.Run(() => IsArchiveOnDisk(entry.FullPath));
        switch (BrowserEntryRouting.DecideBrowserRow(IsBusy, insideArchive, entry.IsFolder, entry.Name, () => isArchive))
        {
            case RowOpenAction.OpenFolder:
                NavigateIntoFolder(entry);
                break;
            case RowOpenAction.OpenArchive:
                await EnterBrowseModeAsync(entry.FullPath);
                break;
            case RowOpenAction.DrillIntoNestedArchive:
                await NavigateIntoNestedArchiveAsync(entry);
                break;
            case RowOpenAction.Preview:
                await PreviewBrowserEntryAsync(entry);
                break;
            case RowOpenAction.ExtractWithWarning:
                await ExtractSingleBrowserEntryWithWarningAsync(entry);
                break;
        }
    }

    private static bool IsArchiveOnDisk(string path) =>
        File.Exists(path) && ArchiveFormatDetector.Detect(path) != ArchiveFormat.Unknown;

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
                    string[] segments = CurrentFolderPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
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
                    string[] segments = CurrentFolderPath.Split('/');
                    CurrentFolderPath = string.Join('/', segments.Take(localIndex));
                }
                break;
        }
        RefreshCurrentFolder();
    }

    // Only a real selection is the user's next action: the list also reports an empty selection
    // when its rows are replaced, e.g. as the browser closes after "Extract all" + delete-after.
    public void SetSelectedBrowserEntries(IReadOnlyList<ArchiveEntryViewModel> entries)
    {
        if (entries.Count > 0)
            ClearOutcome();
        SelectedBrowserEntries = entries;
    }

    partial void OnSelectedBrowserEntriesChanged(IReadOnlyList<ArchiveEntryViewModel> value) => RaiseFooter();

    partial void OnCurrentFolderEntriesChanged(ObservableCollection<ArchiveEntryViewModel> value) => RaiseFooter();

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
        // T-F98: a nested level's own root pops back to its parent level before the outermost
        // (real, on-disk) archive's root climbs into real folders (T-F107) — BrowseNavigation.
        switch (BrowseNavigation.DecideUp(BrowseScope, CurrentFolderPath, _browseStack.Count, BrowsedArchivePath))
        {
            case BrowseUpStep.ArchiveParentFolder:
                NavigateToBreadcrumbSegment(BreadcrumbSegments.Count - 2);
                break;

            case BrowseUpStep.PopNestedLevel:
                string? childScopeDir = _currentNestedScopeDir;
                NestedBrowseLevel parentLevel = _browseStack.Pop();
                BrowsedArchivePath = parentLevel.ArchivePath;
                CurrentFolderPath = parentLevel.CurrentFolderPath;
                _currentLevelDisplayName = parentLevel.DisplayName;
                _archiveIndex = parentLevel.ArchiveIndex;
                _nestedBreadcrumbAncestry = parentLevel.BreadcrumbAncestry;
                _currentNestedScopeDir = parentLevel.ScopeDir;
                SetBrowseLevel(parentLevel.Encryption, parentLevel.IsZip);
                RefreshCurrentFolder();
                if (childScopeDir is not null)
                    NestedArchiveCache.DeleteScope(childScopeDir);
                break;

            case BrowseUpStep.ContainingFolder:
                string containingFolder = Path.GetDirectoryName(BrowsedArchivePath)!;
                BrowsedArchivePath = null;
                SetBrowseLevel(null, isZip: false);
                BrowseScope = ArchiveBrowseScope.RealFileSystem;
                CurrentFolderPath = containingFolder;
                RefreshCurrentFolder();
                break;

            case BrowseUpStep.RealParentFolder:
                CurrentFolderPath = Path.GetDirectoryName(CurrentFolderPath)!;
                RefreshCurrentFolder();
                break;

            case BrowseUpStep.ThisPc:
                if (BrowseScope == ArchiveBrowseScope.Archive)
                {
                    BrowsedArchivePath = null;
                    SetBrowseLevel(null, isZip: false);
                }
                BrowseScope = ArchiveBrowseScope.ThisPc;
                CurrentFolderPath = string.Empty;
                RefreshCurrentFolder();
                break;

            case BrowseUpStep.None:
                break; // CanNavigateUp() is false at "This PC" — unreachable in practice.
        }
    }

    private bool CanExtractSelectedFromBrowser() =>
        !IsBusy && BrowsedArchivePath is not null && SelectedBrowserEntries.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExtractSelectedFromBrowser))]
    private Task ExtractSelectedFromBrowserAsync() =>
        RunExtractAsync([BrowsedArchivePath!], [.. SelectedBrowserEntries.Select(e => e.FullPath)], fromBrowser: true,
            allowDeleteAfter: Location.OffersDeleteAfter);

    private bool CanExtractAllFromBrowser() => !IsBusy && BrowsedArchivePath is not null;

    [RelayCommand(CanExecute = nameof(CanExtractAllFromBrowser))]
    private Task ExtractAllFromBrowserAsync() =>
        RunExtractAsync([BrowsedArchivePath!], selectedEntryPaths: null, fromBrowser: true,
            allowDeleteAfter: Location.OffersDeleteAfter);

    // T-F210: back to create mode (the button and Esc). Off while a listing, drill-in or preview
    // is still running, so that work never lands in a closed browser.
    private bool CanCloseArchive() => IsBrowsingArchive && !IsBusy && !_browseWork.InFlight;

    [RelayCommand(CanExecute = nameof(CanCloseArchive))]
    private void CloseArchive()
    {
        ClearOutcome();
        CloseArchiveCore();
    }

    private void CloseArchiveCore()
    {
        ResetNestedBrowseStack();
        _browsePasswords.Clear();
        _archiveIndex = ArchiveTreeIndex.Build([]);
        BrowsedArchivePath = null;
        BrowseScope = ArchiveBrowseScope.Archive;
        CurrentFolderPath = string.Empty;
        CurrentFolderEntries = [];
        SelectedBrowserEntries = [];
        BreadcrumbSegments = [];
        SetBrowseLevel(null, isZip: false);
        DeleteAfterOperation = false;
        IsBrowsingArchive = false;
    }

    // T-F241: the App's Test, through the same router call Explorer, Shell and the CLI use. ZIP
    // only (TestArchiveVisibility); "no errors" only when the archive was really tested (T-F274).
    private bool CanTestBrowsedArchive() => !IsBusy && BrowsedArchivePath is not null && _browsedIsZip;

    [RelayCommand(CanExecute = nameof(CanTestBrowsedArchive))]
    private async Task TestBrowsedArchiveAsync()
    {
        string archivePath = BrowsedArchivePath!;
        _cts = new CancellationTokenSource();
        IsBusy = true;
        CancelCommand.NotifyCanExecuteChanged();
        Progress = 0;
        bool wasCancelled = false;
        try
        {
            StatusMessage = _res.GetString("StatusTesting");
            var progress = new Progress<ProgressReport>(r => Progress = r.Percent);
            ArchiveResult result = await _extractionRouter.TestAsync([archivePath], progress, BrowsePasswordResolver(archivePath), _cts.Token);
            _browsePasswords.Complete(archivePath, result);
            _logService.Info($"Test completed — {archivePath} — {result.Outcome}");
            foreach (ArchiveError error in result.Errors)
                _logService.Error($"{error.SourcePath} — {error.Message}");
            if (result.Outcome == OperationOutcome.Completed)
                await _dialogService.ShowInfoAsync(_res.GetString("TestResultTitle"), _res.GetString("TestNoErrorsFound"));
            else
                await _dialogService.ShowOperationSummaryAsync("Test", result);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            StatusMessage = _res.GetString("StatusCancelled");
        }
        catch (Exception ex)
        {
            _logService.Error("Unexpected error during test", ex);
            await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"), ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
        // T-F70: busy until the cancelled line has been seen.
        if (wasCancelled)
            await Task.Delay(2000);
        IsBusy = false;
        StatusMessage = _res.GetString("StatusReady");
        // T-F277: kept as the footer result, as a cancelled Compress/Extract is (T-F211).
        if (wasCancelled)
            SetOutcome(_res.GetString("StatusCancelled"), null, null, "Test");
    }

    // T-F146: scans the current selection if any entries are checked, otherwise the whole open
    // archive — one combined button rather than separate Selected/All variants (keeps the browse
    // command row from getting crowded; this is a lower-frequency diagnostic action, not a bulk
    // Extract). BrowsedArchivePath is the correct target even N levels deep into a nested archive
    // (T-F98) — it already resolves to whatever the browse stack currently has open, real file or
    // NestedArchiveCache temp path, exactly what ExtractSelected/ExtractAll above use identically.
    private bool CanScanArchiveFromBrowser() => !IsBusy && BrowsedArchivePath is not null;

    [RelayCommand(CanExecute = nameof(CanScanArchiveFromBrowser))]
    private async Task ScanArchiveFromBrowserAsync()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        CancelCommand.NotifyCanExecuteChanged();
        Progress = 0;
        bool wasCancelled = false;
        try
        {
            var options = new AntivirusScanOptions
            {
                ArchivePaths = [BrowsedArchivePath!],
                SelectedEntryPaths = SelectedBrowserEntries.Count > 0
                    ? [.. SelectedBrowserEntries.Select(e => e.FullPath)]
                    : null,
                ResolvePasswordAsync = BrowsePasswordResolver(BrowsedArchivePath!),
            };

            string scanningLabel = _res.GetString("ScanResultDialogTitle");
            StatusMessage = scanningLabel;
            // T-F151: r.CurrentFile now names the actual entry being scanned (not just the
            // archive path) — surfacing it here matters more now that a single large entry's
            // AmsiScanBuffer call can run for several real seconds under the raised 256 MiB cap.
            var progress = new Progress<ProgressReport>(r =>
            {
                Progress = r.Percent;
                StatusMessage = r.CurrentFile is null ? scanningLabel : $"{scanningLabel} — {ForDisplay(r.CurrentFile)}";
            });

            ThreatScanResult result = await _antivirusScanService.ScanAsync(options, progress, _cts.Token);
            _logService.Info($"Scan completed — {BrowsedArchivePath} — {result.OverallVerdict}");
            await _dialogService.ShowThreatScanResultAsync(result);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            StatusMessage = _res.GetString("StatusCancelled");
        }
        catch (Exception ex)
        {
            _logService.Error("Unexpected error during scan", ex);
            await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"), ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
        // T-F277: the same cancel path as Test.
        if (wasCancelled)
            await Task.Delay(2000);
        IsBusy = false;
        StatusMessage = _res.GetString("StatusReady");
        if (wasCancelled)
            SetOutcome(_res.GetString("StatusCancelled"), null, null, "Scan");
    }

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
        await RunExtractAsync([BrowsedArchivePath], [entry.FullPath], destDir, fromBrowser: true,
            allowDeleteAfter: Location.OffersDeleteAfter);
    }

    // T-F97: previewable file types (PreviewPolicy) skip the full Extract ceremony (progress,
    // summary dialog) — silently extract to a throwaway PreviewCache scope and open with the OS
    // default handler, with only a status-line change to indicate it's happening. Reuses the
    // real IExtractionRouter pipeline (not a lightweight shortcut) so T-F49's whole-archive
    // pre-scan and MOTW propagation both still apply unchanged — see DECISIONS.md's T-F97 entry.
    public async Task PreviewBrowserEntryAsync(ArchiveEntryViewModel entry)
    {
        if (BrowsedArchivePath is null) return;
        using IDisposable work = _browseWork.Begin();

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
                ResolvePasswordAsync = BrowsePasswordResolver(BrowsedArchivePath),
            };

            ArchiveResult result = await _extractionRouter.ExtractAsync(options);
            _browsePasswords.Complete(BrowsedArchivePath, result);
            LogWarnings(result);
            if (!result.Success || result.CreatedFiles.Count == 0)
            {
                await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"),
                    (result.Errors.Count > 0 ? CoreMessageText.Of(result.Errors[0]) : null) ?? _res.GetString("StatusIssues"));
                return;
            }

            // ArchiveResult.CreatedFiles lists per-archive destination folders, not individual
            // extracted file paths (see ZipArchiveService/TarSandboxedService) — the previewed
            // entry's actual on-disk path has to be computed from the scope dir + entry path.
            // T-F242 item 6: never hand ShellExecute a path outside the scope (an absolute or ".."
            // entry name).
            string? previewFilePath = BrowserEntryRouting.ResolveInScope(scopeDir, entry.FullPath);
            if (previewFilePath is null || !await _dialogService.OpenFileWithDefaultAppAsync(previewFilePath))
                await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"), _res.GetString("StatusIssues"));
        }
        catch (Exception ex)
        {
            _logService.Error("Preview failed", ex);
            await _dialogService.ShowErrorAsync(_res.GetString("DialogErrorTitle"), ex.Message);
        }
        finally
        {
            StatusMessage = _res.GetString("StatusReady");
        }
    }

    private Func<PasswordPromptInfo, Task<PasswordDecision>> BrowsePasswordResolver(string archivePath) =>
        _browsePasswords.Wrap(archivePath, info => _dialogService.ShowPasswordPromptAsync(info, canApplyToRemaining: false));

    /// <summary>T-F200: forgets the browse session's passwords (the window is closing).</summary>
    public void ForgetBrowsePasswords() => _browsePasswords.Clear();

    private async Task RunCleanupAsync(IEnumerable<string> paths)
    {
        StatusMessage = _res.GetString("StatusCleaningUp");
        // T-F207/T-F242: Recycle Bin where the volume has one, an explicit confirmation before
        // anything is deleted permanently, and every source still on disk is reported.
        RecycleResult recycled = await _sourceRecycler.DeleteAsync(paths, _dialogService.ShowPermanentDeleteConfirmAsync);
        // T-F302: a row whose source is gone would only fail the next run ("source does not
        // exist"); the tick, given for this run's sources, does not carry into the next list.
        foreach (string path in recycled.Deleted)
            RemovePath(path);
        DeleteAfterOperation = false;
        if (recycled.NotDeleted.Count == 0)
            return;
        foreach (string path in recycled.NotDeleted)
            _logService.Warn($"Not deleted after operation: {path}");
        await _dialogService.ShowNotDeletedAsync(recycled.NotDeleted);
    }

    private void UpdateOperationStatus(ProgressReport report)
    {
        if (_operationStopwatch is null) return;

        if (report.Phase == ProgressPhase.CheckingArchive)
        {
            _checkingArchive = true;
            StatusMessage = $"{_operationStatusPrefix}  ·  {_res.GetString("StatusCheckingArchive")}";
            return;
        }
        if (_checkingArchive)
        {
            _checkingArchive = false;
            _estimateStart = _operationStopwatch.Elapsed;
            _estimateStartPercent = report.Percent;
        }

        string speedPart = string.Empty;
        string etaPart = string.Empty;

        if (report.TotalBytes > 0)
        {
            double smoothedBytesPerSec = _speedSampler?.Sample(report.BytesTransferred, DateTime.UtcNow) ?? 0;
            if (smoothedBytesPerSec >= 1)
                speedPart = ProgressText.Speed(smoothedBytesPerSec);
            etaPart = ProgressText.Remaining(_operationStopwatch.Elapsed - _estimateStart, report.Percent, _estimateStartPercent);
        }

        var sb = new System.Text.StringBuilder(_operationStatusPrefix);
        if (speedPart.Length > 0) sb.Append($"  ·  {speedPart}");
        if (etaPart.Length > 0)   sb.Append($"  ·  {etaPart}");
        StatusMessage = sb.ToString();
    }

    private bool CanArchive() => !IsBusy && FileItems.Count > 0
        && _encryptionPassword.AllowsCompress(EncryptWithPassword, SelectedContainerFormat);
    private bool CanExtract() => !IsBusy && _listActions.CanExtract;
    private bool CanOperate() => !IsBusy;

    public void AddPaths(IEnumerable<string> paths)
    {
        (IReadOnlyList<string> added, int alreadyListed) = PendingPaths.Split(FileItems.Select(x => x.FullPath), paths);
        foreach (string path in added)
        {
            // T-F232: one unreadable path used to throw out of here, dropping the rest of an
            // activation's list or escaping into the drag-drop handler.
            if (FileItem.TryCreate(path) is { } item)
                FileItems.Add(item);
            else
                _logService.Warn($"Skipped unreadable path: {path}");
        }
        // T-F278: a drop of only listed items used to change nothing on screen.
        if (alreadyListed > 0)
            SetOutcome(string.Format(System.Globalization.CultureInfo.CurrentCulture, _res.GetString("AlreadyInListLine"), alreadyListed),
                null, null, string.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private void Clear()
    {
        foreach (FileItem item in FileItems)
            item.Dispose();
        FileItems.Clear();
        DeleteAfterOperation = false;
    }

    public void RemovePath(string path)
    {
        FileItem? item = FileItems.FirstOrDefault(x => string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            FileItems.Remove(item);
            item.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task BrowseFilesAsync()
    {
        IReadOnlyList<string> paths = await _dialogService.PickFilesAsync();
        AddPaths(paths);
    }

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task BrowseFolderAsync()
    {
        IReadOnlyList<string> paths = await _dialogService.PickFoldersAsync();
        AddPaths(paths);
    }
}
#pragma warning restore CA1001
