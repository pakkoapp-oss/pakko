using System.Linq;
using System.Windows.Input;
using Archiver.App.Core;
using Archiver.App.Models;
using Archiver.App.Services;
using Archiver.App.ViewModels;
using Archiver.Core.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Archiver.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    // T-F106: EnsureWindow's Activate() returns before RootGrid's first Loaded/layout pass
    // completes — mutating ViewModel state (FileItems, IsBrowsingArchive) synchronously right
    // after Activate() realizes ListView containers against an incomplete layout, leaving rows
    // permanently blank (does not self-correct on resize, unlike the unrelated T-F05 blank-row
    // bug). App.xaml.cs::HandleActivation routes every such mutation through this gate instead.
    public DeferredActionGate ActivationGate { get; } = new();

    public ICommand TrayOpenCommand { get; }
    public ICommand TrayAboutCommand { get; }
    public ICommand TrayExitCommand { get; }
    public ICommand TrayLeftClickCommand { get; }
    public ICommand HashFilesCommand { get; }


    public MainWindow()
    {
        TrayOpenCommand = new RelayCommand(() =>
        {
            this.Activate();
            
        });
        TrayAboutCommand = new AsyncRelayCommand(async () =>
        {
            this.Activate();
            await App.Services.GetRequiredService<IDialogService>().ShowAboutAsync();
        });
        TrayExitCommand = new RelayCommand(() => Application.Current.Exit());
        HashFilesCommand = new AsyncRelayCommand(async () =>
            await App.Services.GetRequiredService<IDialogService>().ShowFileHashAsync());
        TrayLeftClickCommand = new RelayCommand(() =>
        {
            if (this.AppWindow.IsVisible)
                this.AppWindow.Hide();
            else
                this.Activate();
        });

        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        // Widened from the original 800x700 (design review 2026-07-13): a file/archive listing
        // is inherently tabular — the Name column needs width far more than the window needs
        // height, matching every reference file manager (Explorer, NanaZip, 7-Zip all default to
        // wide-not-square windows). The old near-square size truncated long/nested archive entry
        // names more aggressively than necessary.
        // T-F106: height raised from 650 to 900 — at 650, the pending-list mode's Archive Options
        // panel (Mode/Name/Format/Compression, 4 rows) plus Shared Options/action buttons/status
        // bar could collectively demand more height than the window had, collapsing the file
        // table's Star row to 0 (see RootGrid's RowDefinitions comment in MainWindow.xaml for the
        // full root-cause account). 900 leaves real room for the table even with every optional
        // row visible.
        this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 900));

        // T-F106: without an explicit floor, the window could be shrunk by the user to a height
        // where content below the file table (Shared Options' two checkboxes, the status bar)
        // gets clipped off the bottom of the window instead of the table itself collapsing —
        // confirmed on-device: at an earlier 700px floor, "Готово"/the checkboxes reported
        // (0,0,0) `ui_find` bounds even though the table itself stayed visible. 850 was measured
        // by testing at increasing heights until every row — table, options, checkboxes, status
        // bar — reported non-zero bounds simultaneously.
        if (this.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 900;
            presenter.PreferredMinimumHeight = 850;
        }
        // On-device verification relies on a fresh Deploy.ps1 having actually replaced the
        // installed binary — a bare "Build succeeded" log does not prove that (see CLAUDE.md's
        // stale-MSIX gotcha). Reading the running assembly's own file timestamp and showing it in
        // the title bar makes every screenshot self-certifying: if the timestamp isn't "just now,"
        // the deploy didn't actually pick up the latest change.
        var buildTime = System.IO.File.GetLastWriteTime(
            System.Reflection.Assembly.GetExecutingAssembly().Location);
        this.AppWindow.Title = $"Pakko — build {buildTime:yyyy-MM-dd HH:mm:ss}";

        this.AppWindow.SetIcon("Assets/Square44x44Logo.ico");

        this.Activated += OnFirstActivated;
        RootGrid.Loaded += RootGrid_Loaded;
        this.Closed += (_, _) =>
        {
            TrayIcon.Dispose();
            ActivationGate.Cancel();
            PreviewCache.DeleteAll();
            NestedArchiveCache.DeleteAll();
        };
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        this.Activated -= OnFirstActivated;
        TrayIcon.XamlRoot = Content.XamlRoot;
    }

    // T-F106: opens the gate so activation-time ViewModel mutations queued before this point
    // flush now, and any later ones run immediately. NOTE: empirically, this alone does NOT fix
    // the blank-row symptom (nor does gating on Window.Activated or CompositionTarget.Rendering,
    // both also tried) — see DECISIONS.md's T-F106 entry. Kept as a real, independently-correct
    // improvement (never mutate ViewModel state before the first layout pass), but the visual bug
    // itself has a different, not-yet-identified root cause.
    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        ActivationGate.Open();
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add to list";
        e.Handled = true;
    }

    private async void FileList_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = new List<string>();
            foreach (var item in items)
            {
                var path = item switch
                {
                    Windows.Storage.StorageFile file => file.Path,
                    Windows.Storage.StorageFolder folder => folder.Path,
                    _ => item.Path
                };
                System.Diagnostics.Debug.WriteLine($"[Drop] name={item.Name} path={path}");
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }
            ViewModel.AddPaths(paths);
        }
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is FileItem fileItem)
            ViewModel.RemovePath(fileItem.FullPath);
    }

    // T-F05: Archive Browser — double-clicking a recognized archive in the pending-selection
    // list enters the browser view instead of doing nothing. Gate uses ArchiveFormatDetector
    // (magic-byte, same ground truth ExtractionRouter itself uses) rather than FileItem.Type's
    // extension-derived string, which exists only for the batch Archive/Extract UI text and would
    // misclassify a renamed/extensionless archive.
    private async void PendingList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { DataContext: FileItem item })
            return;
        if (item.Type == "Folder" || !System.IO.File.Exists(item.FullPath))
            return;
        if (ArchiveFormatDetector.Detect(item.FullPath) == Archiver.Core.Models.ArchiveFormat.Unknown)
            return;

        await ViewModel.EnterBrowseModeAsync(item.FullPath);
    }

    private void ArchiveBreadcrumb_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args) =>
        ViewModel.NavigateToBreadcrumbSegment(args.Index);

    private void ArchiveBrowserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView listView) return;
        ViewModel.SetSelectedBrowserEntries([.. listView.SelectedItems.OfType<ArchiveEntryViewModel>()]);
    }

    // Double-click a folder row descends into it (breadcrumb appends a segment, selection
    // clears); double-click a file row extracts just that entry, reusing the same
    // RunExtractAsync sequence as every other extraction path.
    private async void ArchiveBrowserList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { DataContext: ArchiveEntryViewModel entry })
            return;

        if (entry.IsFolder)
        {
            ViewModel.NavigateIntoFolder(entry);
            return;
        }

        // T-F107: a "file" double-tapped while browsing real folders/drives (not inside an
        // archive) isn't extractable — there's no BrowsedArchivePath to extract from. If it's
        // itself a recognized archive, open it fresh (same trust level as the pending-list
        // double-click gate below — not T-F98's deferred nested-archive-drill-down, which is
        // about archives found *inside* the currently open archive). A plain file is a no-op.
        if (ViewModel.BrowseScope != ArchiveBrowseScope.Archive)
        {
            if (ArchiveFormatDetector.Detect(entry.FullPath) != Archiver.Core.Models.ArchiveFormat.Unknown)
                await ViewModel.EnterBrowseModeAsync(entry.FullPath);
            return;
        }

        // T-F98: an archive found inside the currently open archive drills in, extracting just
        // that entry to a temp scope and browsing it — checked before PreviewPolicy since a real
        // archive extension is never also a previewable one, but ordering here is for clarity,
        // not correctness (the two extension sets are already disjoint).
        if (ArchiveFormatDetector.IsRecognizedArchiveExtension(entry.Name))
        {
            await ViewModel.NavigateIntoNestedArchiveAsync(entry);
        }
        // T-F97: a previewable file type opens silently via the OS default handler instead of
        // running the full Extract flow.
        else if (PreviewPolicy.IsPreviewable(entry.Name))
        {
            await ViewModel.PreviewBrowserEntryAsync(entry);
        }
        else
        {
            await ViewModel.ExtractSingleBrowserEntryWithWarningAsync(entry);
        }
    }
}
