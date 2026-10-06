using System.Linq;
using System.Windows.Input;
using Archiver.App.Core;
using Archiver.App.Services;
using Archiver.App.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;

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
    public ICommand AboutCommand { get; }
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
        // T-F308 item 4: the toolbar's About gives focus back to the button that opened it (the
        // dialog left it on "Add files"); from the tray there is nothing to give it back to.
        AboutCommand = new AsyncRelayCommand(async () =>
        {
            var opener = FocusManager.GetFocusedElement(Content.XamlRoot) as Control;
            await App.Services.GetRequiredService<IDialogService>().ShowAboutAsync();
            // After the dialog's own focus handling, which runs once ShowAsync has returned.
            if (opener is not null)
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => opener.Focus(FocusState.Keyboard));
        });
        TrayExitCommand = new RelayCommand(() => Application.Current.Exit());
        TrayLeftClickCommand = new RelayCommand(() =>
        {
            if (this.AppWindow.IsVisible)
                this.AppWindow.Hide();
            else
                this.Activate();
        });

        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        HashFilesCommand = new AsyncRelayCommand(async () =>
            await App.Services.GetRequiredService<IDialogService>().ShowFileHashAsync([.. ViewModel.FileItems.Select(x => x.FullPath)]));
        // Widened from the original 800x700 (design review 2026-07-13): a file/archive listing
        // is inherently tabular — the Name column needs width far more than the window needs
        // height, matching every reference file manager (Explorer, NanaZip, 7-Zip all default to
        // wide-not-square windows). The old near-square size truncated long/nested archive entry
        // names more aggressively than necessary.
        // T-F106: height raised from 650 to 900 — at 650, the pending-list mode's Archive Options
        // panel (Mode/Name/Format/Compression, 4 rows) plus Shared Options/action buttons/status
        // bar could collectively demand more height than the window had, collapsing the file
        // table's Star row to 0 (see RootGrid's RowDefinitions comment in MainWindow.xaml for the
        // full root-cause account). 900 left real room for the table even with every optional
        // row visible, but read as needlessly large/near-square — T-F106's responsive-design
        // follow-up (2026-07-17) re-tuned this down to 780 (paired with the table's MinHeight
        // dropping 200->140 below) after re-running the same on-device zero-bounds check this
        // value's history required; see DECISIONS.md's T-F106 entry for the confirmed numbers.
        // T-F199/T-F224: 720 fits a 1366x768 screen's work area; the options now scroll.
        this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 720));
        PlaceAwayFromOtherPakkoWindows();

        // T-F106: without an explicit floor, the window could be shrunk by the user to a height
        // where content below the file table (Shared Options' two checkboxes, the status bar)
        // gets clipped off the bottom of the window instead of the table itself collapsing —
        // confirmed on-device: at an earlier 700px floor, "Готово"/the checkboxes reported
        // (0,0,0) `ui_find` bounds even though the table itself stayed visible. 850 was measured
        // by testing at increasing heights until every row — table, options, checkboxes, status
        // bar — reported non-zero bounds simultaneously. Re-tuned down to 780 in the same
        // follow-up noted above, re-verified with the identical on-device method.
        // T-F199/T-F224: the options scroll now (FitOptionsScroll), so the floor only has to hold
        // the title bar, toolbar, the table's 160 px, a slice of options and the footer.
        if (this.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 900;
            presenter.PreferredMinimumHeight = 520;
        }
        RootGrid.SizeChanged += (_, _) =>
        {
            FitOptionsScroll();
            ArrangeCards();
        };
        // The footer grows without a resize (outcome line, progress bar).
        FooterGrid.SizeChanged += (_, _) => FitOptionsScroll();
        // T-F199 step 6: browse mode's breadcrumb, info bar and header share the table's row; the
        // bar opens, closes and rewraps on every level change and resize.
        BrowseBreadcrumbRow.SizeChanged += (_, _) => FitOptionsScroll();
        BrowseInfoBar.SizeChanged += (_, _) => FitOptionsScroll();
        BrowseHeader.SizeChanged += (_, _) => FitOptionsScroll();
        NewArchiveCard.Expanding += (_, _) => ArrangeCards();
        NewArchiveCard.Collapsed += (_, _) => ArrangeCards();
        NewArchiveCard.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => ArrangeCards());

        // T-F199: content extends into the title bar; TitleText shows AppWindow.Title (the build stamp).
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        RootGrid.Loaded += (_, _) => ApplyCaptionColors();
        RootGrid.ActualThemeChanged += (_, _) => ApplyCaptionColors();
        // On-device verification relies on a fresh Deploy.ps1 having actually replaced the
        // installed binary — a bare "Build succeeded" log does not prove that (see CLAUDE.md's
        // stale-MSIX gotcha). Showing the running assembly's compile time in the title bar makes
        // every screenshot self-certifying: if it isn't "just now," the deploy didn't pick up the
        // latest change. T-F218: the compile time (assembly metadata), not the file time, which
        // for an installed MSIX is the install time; T-F198 item 4: no stamp in a Store build.
        this.AppWindow.Title = BuildStamp.Title(
            BuildStamp.Read(System.Reflection.Assembly.GetExecutingAssembly()), IsStoreBuild());
        TitleText.Text = this.AppWindow.Title;

        this.AppWindow.SetIcon("Assets/Square44x44Logo.ico");

        this.Activated += OnFirstActivated;
        RootGrid.Loaded += RootGrid_Loaded;
        this.Closed += (_, _) =>
        {
            TrayIcon.Dispose();
            ActivationGate.Cancel();
            PreviewCache.DeleteOwn();
            NestedArchiveCache.DeleteOwn();
            ViewModel.ForgetBrowsePasswords();
            ViewModel.ClearEncryptionPassword();
        };
        ViewModel.EncryptionPasswordCleared += (_, _) =>
        {
            EncryptPasswordBox.Password = string.Empty;
            EncryptConfirmBox.Password = string.Empty;
        };
        // The options scroll: without this a new message can sit below the fold while Compress is
        // off. After the line's SizeChanged, and one dispatcher turn later: the ScrollViewer's
        // extent grows only after the line has been laid out, and an earlier request is clamped.
        EncryptPasswordMessageText.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Height > 0)
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => EncryptPasswordMessageText.StartBringIntoView());
        };
    }

    // T-F199 step 5: a PasswordBox has no bindable Password; the view model checks every edit and
    // hands back the text without refused characters, like a Windows PIN box.
    private void EncryptPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        string kept = ViewModel.SetEncryptionPassword(EncryptPasswordBox.Password);
        if (kept != EncryptPasswordBox.Password)
            EncryptPasswordBox.Password = kept;
    }

    private void EncryptConfirmBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        string kept = ViewModel.SetEncryptionConfirmation(EncryptConfirmBox.Password);
        if (kept != EncryptConfirmBox.Password)
            EncryptConfirmBox.Password = kept;
    }

    // T-F199/T-F224: an Auto row never scrolls, so the options get what the table's minimum
    // leaves. Pure layout, no view-model state.
    private const double TableMinHeight = 160;

    private void FitOptionsScroll()
    {
        // From the window (RootGrid), not ContentGrid: an overfull ContentGrid reports its desired
        // height, so measuring it would keep a too-tall options panel tall.
        double spacing = ContentGrid.RowSpacing * 3;
        double available = RootGrid.ActualHeight - RootGrid.RowDefinitions[0].ActualHeight
            - ContentGrid.Padding.Top - ContentGrid.Padding.Bottom
            - ToolbarHeight() - FooterGrid.DesiredSize.Height - spacing - TableMinHeight - BrowseChromeHeight();
        OptionsScroll.MaxHeight = System.Math.Max(0, available);
    }

    // The 160 px go to the list's rows, not to what sits above them in browse mode.
    private double BrowseChromeHeight() => ViewModel.IsBrowsingArchive
        ? OuterHeight(BrowseBreadcrumbRow) + OuterHeight(BrowseInfoBar) + OuterHeight(BrowseHeader)
        : 0;

    private static double OuterHeight(FrameworkElement element) =>
        element.ActualHeight > 0 ? element.ActualHeight + element.Margin.Top + element.Margin.Bottom : 0;

    // T-F199: the two option cards sit side by side only when both are open and each gets enough
    // width; otherwise they stack (a collapsed or hidden "New archive" card leaves the other full width).
    private const double SideBySideMinWidth = 960;

    private void ArrangeCards()
    {
        bool newArchiveShown = NewArchiveCard.Visibility == Visibility.Visible;
        bool sideBySide = ContentGrid.ActualWidth >= SideBySideMinWidth && NewArchiveCard.IsExpanded && newArchiveShown;
        Grid.SetColumnSpan(NewArchiveCard, sideBySide ? 1 : 2);
        Grid.SetRow(DestinationCard, sideBySide || !newArchiveShown ? 0 : 1);
        Grid.SetColumn(DestinationCard, sideBySide ? 1 : 0);
        Grid.SetColumnSpan(DestinationCard, sideBySide ? 1 : 2);
    }

    private double ToolbarHeight() =>
        ContentGrid.Children.OfType<FrameworkElement>()
            .Where(e => Grid.GetRow(e) == 0 && e.Visibility == Visibility.Visible)
            .Select(e => e.ActualHeight).DefaultIfEmpty(0).Max();

    // Content extends into the title bar, so its caption buttons follow the theme by hand (as
    // Archiver.OperationUi's window does).
    private void ApplyCaptionColors()
    {
        Microsoft.UI.Windowing.AppWindowTitleBar titleBar = this.AppWindow.TitleBar;
        Windows.UI.Color foreground = RootGrid.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }

    private static bool IsStoreBuild()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.Store;
        }
        catch (System.InvalidOperationException)
        {
            return false; // not running packaged
        }
    }

    // T-F201: a second Pakko window used to open exactly over the first one.
    private void PlaceAwayFromOtherPakkoWindows()
    {
        IReadOnlyList<(int Left, int Top)> others = Win32PakkoWindows.OtherWindowCorners();
        if (others.Count == 0)
            return;
        Windows.Graphics.PointInt32 position = this.AppWindow.Position;
        Windows.Graphics.SizeInt32 size = this.AppWindow.Size;
        Windows.Graphics.RectInt32 work = Microsoft.UI.Windowing.DisplayArea
            .GetFromWindowId(this.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        (int left, int top) = WindowCascade.Place(position.X, position.Y, size.Width, size.Height, others,
            (work.X, work.Y, work.X + work.Width, work.Y + work.Height));
        if (left != position.X || top != position.Y)
            this.AppWindow.Move(new Windows.Graphics.PointInt32(left, top));
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
        e.DragUIOverride.Caption = MainViewModel.DragAddCaption;
        e.Handled = true;
    }

    private async void FileList_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            var paths = new List<string>();
            foreach (IStorageItem? item in items)
            {
                string path = item switch
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
        if (sender is MenuFlyoutItem item && item.DataContext is FileItem fileItem && ViewModel.ClearCommand.CanExecute(null))
            ViewModel.RemovePath(fileItem.FullPath);
    }

    // T-F308 item 2: Delete removes the focused row (not during an operation) and moves focus to
    // the row that took its place, so a second Delete works.
    private void PendingList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Delete || e.OriginalSource is not ListViewItem { Content: FileItem item }
            || !ViewModel.ClearCommand.CanExecute(null))
            return;
        e.Handled = true;
        int index = ViewModel.FileItems.IndexOf(item);
        ViewModel.RemovePath(item.FullPath);
        FocusRowLater(FileListView, System.Math.Min(index, ViewModel.FileItems.Count - 1));
    }

    // Shift+F10 / the menu key on a focused row: the row's own menu sits on the template's Grid,
    // which a keyboard request on the ListViewItem never reaches.
    private void PendingList_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.OriginalSource is ListViewItem { ContentTemplateRoot: FrameworkElement { ContextFlyout: { } flyout } } row
            && ViewModel.ClearCommand.CanExecute(null))
        {
            args.Handled = true;
            flyout.ShowAt(row);
        }
    }

    // T-F308 item 1: Enter opens the focused row like a double-click (ahead of the Multiple-mode
    // selection toggle); Backspace and Alt+Up go up one level. Focus-scoped to the list, so typing
    // in a TextBox never navigates.
    private async void ArchiveBrowserList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool up = e.Key == Windows.System.VirtualKey.Back
            || (e.Key == Windows.System.VirtualKey.Up && IsAltDown());
        if (up)
        {
            e.Handled = true;
            if (ViewModel.NavigateUpCommand.CanExecute(null))
            {
                ViewModel.NavigateUpCommand.Execute(null);
                FocusRowLater(ArchiveBrowserListView, 0);
            }
        }
        else if (e.Key == Windows.System.VirtualKey.Enter && e.OriginalSource is ListViewItem { Content: ArchiveEntryViewModel entry })
        {
            e.Handled = true;
            await ViewModel.OpenBrowserRowAsync(entry);
            FocusRowLater(ArchiveBrowserListView, 0);
        }
    }

    private static bool IsAltDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // After the list's items change, its containers exist only after the next layout pass.
    private void FocusRowLater(ListView list, int index)
    {
        if (index < 0)
            return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (list.ContainerFromIndex(index) is ListViewItem row)
                row.Focus(FocusState.Keyboard);
        });
    }

    private async void PendingList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: FileItem item })
            await ViewModel.OpenPendingRowAsync(item);
    }

    private void ArchiveBreadcrumb_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args) =>
        ViewModel.NavigateToBreadcrumbSegment(args.Index);

    private void ArchiveBrowserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView listView) return;
        ViewModel.SetSelectedBrowserEntries([.. listView.SelectedItems.OfType<ArchiveEntryViewModel>()]);
    }

    private async void ArchiveBrowserList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: ArchiveEntryViewModel entry })
            await ViewModel.OpenBrowserRowAsync(entry);
    }
}
