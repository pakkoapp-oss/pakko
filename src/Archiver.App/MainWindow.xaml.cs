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
        this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 650));
        this.AppWindow.Title = "Pakko";

        this.AppWindow.SetIcon("Assets/Square44x44Logo.ico");

        this.Activated += OnFirstActivated;
        RootGrid.Loaded += RootGrid_Loaded;
        this.Closed += (_, _) =>
        {
            TrayIcon.Dispose();
            ActivationGate.Cancel();
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
            ViewModel.NavigateIntoFolder(entry);
        else
            await ViewModel.ExtractSingleBrowserEntryAsync(entry);
    }
}
