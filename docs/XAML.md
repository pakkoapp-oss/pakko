# XAML.md — UI Structure Reference

> **Historical note:** This file originally contained the bootstrap skeleton for MainWindow.
> The UI is now fully implemented. This file describes the current actual structure.

> **Last verified against `src/Archiver.App/MainWindow.xaml` directly, 2026-09-28** (T-F199's
> redesign: own title bar, toolbar / table / option cards / footer). If you touch
> `MainWindow.xaml`'s row structure, re-verify this section the same way (read the file, don't
> pattern-match the old tree) per `DIAGRAMS.md`'s Ground Truth Rule, which applies just as much to
> this file.

---

## Current MainWindow.xaml Structure

```
Window (ExtendsContentIntoTitleBar, SetTitleBar(AppTitleBar); no Mica, see DECISIONS.md wave 4)
└── RootGrid (RowDefinitions="32,*")
    ├── [tb:TaskbarIcon] — system tray (not in grid flow)
    │
    ├── Row 0: AppTitleBar — Grid (Auto,Auto,*): 16 px app icon + TitleText (AppWindow.Title, the
    │       build stamp; none in a Store build, T-F218/T-F198 item 4). Caption button colors follow
    │       the theme by hand (ApplyCaptionColors).
    │
    └── Row 1: ContentGrid (RowDefinitions="Auto,* MinHeight=160,Auto,Auto", Padding="16,4,16,12",
        │       RowSpacing="12"). The Star row's MinHeight lives on the RowDefinition (T-F106).
        │
        ├── Row 0 (create mode): toolbar Grid (Auto,Auto,*,Auto,Auto) — Add Files / Add Folder /
        │       (spacer) / Hash / About. Visibility=IsPendingListVisibility.
        ├── Row 0 (browse mode): toolbar Grid (*,Auto,Auto,Auto,Auto) — (spacer) / Test archive
        │       (ZIP only, TestArchiveVisibility, T-F241) / Scan / Close archive (Esc accelerator,
        │       T-F210) / About. Visibility=IsBrowsingArchiveVisibility.
        │
        ├── Row 1 (create mode): Grid (RowDefinitions="Auto,*") — File table.
        │   ├── Header: Border -> Grid (*,80,100,90,140) — Name/Type/Size/Crc/Modified, each a
        │   │       sortable Button (SortByCommand)
        │   └── Body: Grid
        │       ├── ListView FileListView (SelectionMode=None, AllowDrop, DoubleTapped ->
        │       │       PendingList_DoubleTapped); row ContextFlyout "Remove from list"
        │       └── StackPanel empty state (T-F199 board 3) — drop zone WITH its own drop handlers
        │               and its own Add Files / Add Folder buttons; hit-testable, so it takes the
        │               drop itself (see "Empty-state overlay" below). IsFileListEmptyVisibility.
        │
        ├── Row 1 (browse mode): Grid (RowDefinitions="Auto,Auto,Auto,*") — Archive Browser.
        │   ├── BrowseBreadcrumbRow (Auto,*,Auto) — Up button (NavigateUpCommand, T-F107),
        │   │       BreadcrumbBar, encryption badge (weakest method present, EncryptionBadgeVisibility)
        │   ├── BrowseInfoBar — InfoBar (IsOpen/Severity/Message bound; encrypted count, AE-2 empty
        │   │       CRC note, ZipCrypto warning as Warning severity, "outside the archive" note)
        │   ├── BrowseHeader — Border -> Grid (Auto,*,100,100,90,140), non-sortable TextBlocks
        │   └── ListView ArchiveBrowserListView (SelectionMode=Multiple, explicit
        │           VirtualizingStackPanel — a known deviation from CLAUDE.md's ListView rule,
        │           see T-F05; SelectionChanged + DoubleTapped). ItemTemplate: icon /
        │           Grid(lock FontIcon when IsEncrypted, named for UIA; Name) / Size / Packed / Crc /
        │           Modified
        │
        ├── Row 2: ScrollViewer OptionsScroll (OptionsVisibility/OptionsOpacity; hidden outside an
        │   │       archive in browse mode). MaxHeight set in code-behind (FitOptionsScroll).
        │   └── OptionsGrid (*,* columns; Auto,Auto rows) — two cards, placed by ArrangeCards:
        │       ├── NewArchiveCard — Expander (collapsed with a summary for an archives-only list,
        │       │       T-F199 board 8; NewArchiveCardVisibility). One Grid (Auto,*) with 5 rows:
        │       │       Mode (One archive / Separate archives), Name (placeholder = Core's auto
        │       │       name, T-F264), Format + Compression ComboBoxes (LabeledBy their captions),
        │       │       "Encrypt with password" CheckBox or, for a tar format, the "ZIP only" note
        │       │       in the same cell, then the inline password panel (T-F199 step 5): note,
        │       │       EncryptPasswordBox, EncryptConfirmBox, the red message line, "Show
        │       │       password", the rule hint.
        │       └── DestinationCard — Border (card), Grid (Auto,*) with 6 rows: title, destination
        │               (Up button NavigateDestinationUpCommand, read-only TextBox, "..."), "If file
        │               exists" ComboBox (Overwrite/Skip/Rename/Ask), Open destination, delete-after
        │               CheckBox (words from DeleteAfterLabel, T-F207) + Recycle Bin note.
        │
        └── Row 3: FooterGrid (*,Auto,Auto,Auto,Auto, MinHeight=40)
            ├── Col 0: StackPanel — ProgressBar (while running); footer line (FooterText from
            │       App.Core FooterLine.Pick: result, browse selection, or create preview) with
            │       "Show in folder" / "Details..." HyperlinkButtons (T-F211); StatusMessage line.
            ├── Col 1: Cancel (while running)
            ├── Col 2: Clear (create mode)
            ├── Create mode: Extract (inside a Border that carries the "why disabled" tooltip,
            │       T-F212) and Compress to {format} — columns and accent style come from
            │       ExtractButtonColumn/ArchiveButtonColumn/*ButtonStyle, so the primary one is
            │       always column 4 (PrimaryActionPolicy).
            └── Browse mode: Extract Selected (col 3), Extract All (col 4, accent).
                    BrowseExtractActionsVisibility (hidden outside the archive).
```

**Code-behind layout helpers (`MainWindow.xaml.cs`, layout only, no view-model state):**
- `FitOptionsScroll` — an `Auto` row never scrolls, so `OptionsScroll.MaxHeight` is what the window
  (measured from `RootGrid`, not the overfull `ContentGrid`) leaves after the title bar, toolbar,
  footer, the table's 160 px and, in browse mode, the breadcrumb/info bar/header. Rerun on
  `RootGrid`, `FooterGrid`, breadcrumb, info bar and header `SizeChanged`.
- `ArrangeCards` — the cards sit side by side only from 960 px and when "New archive" is shown and
  expanded; otherwise they stack.
- Window: default 1100x720, floor 900x520 (`OverlappedPresenter.PreferredMinimum*`, T-F224).
- A `PasswordBox` has no bindable `Password`: `PasswordChanged` hands the text to the view model,
  which returns it without refused characters (PIN-box behavior).
- Keyboard (T-F308): `FileListView.PreviewKeyDown` (Delete removes the focused row, focus moves to
  the next) and `ContextRequested` (Shift+F10 shows the row template Grid's own `ContextFlyout` at the
  `ListViewItem`, which a keyboard request never reaches); `ArchiveBrowserListView.PreviewKeyDown`
  (Enter opens the row ahead of the Multiple-mode selection toggle, Backspace/Alt+Up go up) - list
  handlers, not window `KeyboardAccelerator`s, so typing in a TextBox never navigates.
  `FooterGrid` has `TabFocusNavigation="Local"` and a `TabIndex` on every button; the two primary
  buttons bind theirs to the same `*ButtonColumn` as their column, so Tab follows the screen order.
  The toolbar's About uses `AboutCommand`, which gives focus back to the button (the tray keeps
  `TrayAboutCommand`).
- Header sort arrows (T-F220 item 4) are `FontIcon` siblings in the header columns, not part of the
  header Button's `Content` - `x:Uid` sets `.Content` and would overwrite it.

**Two distinct "Up" buttons, easy to conflate:** browse mode's Up button (`NavigateUpCommand`)
climbs *inside* the archive/real-filesystem browse stack (T-F98/T-F107). The destination card's Up
button (`NavigateDestinationUpCommand`) walks the chosen **destination** folder up one level via
`Path.GetDirectoryName`. Both use the identical Segoe MDL2 `&#xE74A;` glyph, but they bind to
different commands with different `CanExecute` gates and different UIA names — a future edit to one
must not assume it covers the other.

---

## WinUI 3 Constraints Learned

**`Window` is not `FrameworkElement`:**
- Do NOT use `x:Load` on direct children of `Window` — causes `FindName` CS1061
- Use `Visibility` binding instead of `x:Load`

**Empty-state overlay pattern (T-F199):**
```xml
<Grid>
    <ListView AllowDrop="True" DragOver="FileList_DragOver" Drop="FileList_Drop"/>
    <StackPanel Background="Transparent" AllowDrop="True"
                DragOver="FileList_DragOver" Drop="FileList_Drop"
                Visibility="{x:Bind ViewModel.IsFileListEmptyVisibility, Mode=OneWay}">
        <!-- hint text + its own Add Files / Add Folder buttons -->
    </StackPanel>
</Grid>
```
The overlay has buttons, so it must be hit-testable (the old `IsHitTestVisible="False"` would make
them dead); it takes drops itself with the same handlers. `Background="Transparent"` (not null)
is what makes the empty space between its children accept the drop.

**No `IsSharedSizeScope`/`SharedSizeGroup` (WPF-only):** unlike WPF, `Microsoft.UI.Xaml.Controls.Grid`
has no `IsSharedSizeScope` property and `ColumnDefinition` has no `SharedSizeGroup` — both are
silently rejected by the XAML compiler (`XamlCompiler.exe` exits 1 with no readable diagnostic
piped through `dotnet build`; the actual cause only showed up by reverting the change and
confirming the pre-existing file still built). To align a label column across several rows that
share the same `Visibility` binding (e.g. Archive Options' Mode/Name/Format/Compression rows),
put them all in **one `Grid`** with `ColumnDefinitions="Auto,*"` and one row per item
(`Grid.Row="0..n"`) instead of separate per-row `Grid`s/`StackPanel`s — a single Grid's `Auto`
column width is already computed as the max desired width across every row in that same Grid, so
labels naturally align without any WPF-only API. Found fixing a real bug this way (2026-07-16):
`ModeLabel`/`ArchiveNameLabel` had a hardcoded `Width="46"` sized for English "Mode:"/"Name:";
Ukrainian "Режим:" is longer and `CaptionTextBlockStyle` inherits `TextWrapping="Wrap"` from
`BodyTextBlockStyle`, so the colon wrapped onto its own line. Removing the fixed `Width` and
merging Mode/Name/Format/Compression into one shared-column Grid fixed both the wrap and the
inconsistent left edges — see `DECISIONS.md`.

**A child control's `MinHeight` does not force a Grid's `*` row to grow (T-F106):** giving the
file-table `ListView` its own `MinHeight` does nothing for the *row* it sits in — enough sibling
`Auto` rows can still clamp the Star row to 0, silently rendering every list item within zero
available height. Put `MinHeight` on the `RowDefinition` itself instead
(`ContentGrid`'s Row 1: `<RowDefinition Height="*" MinHeight="160"/>`; see `DECISIONS.md`'s
T-F106 entries for the history). Since T-F199/T-F224 the rows below the table no longer need a tall
window: the options sit in a `ScrollViewer` whose `MaxHeight` code-behind computes from the space
the table's minimum leaves (`FitOptionsScroll` — an `Auto` row never scrolls on its own), so the
window floor is `900x520` and the default `1100x720`. Measure that space from `RootGrid`, not from
`ContentGrid`: an overfull `ContentGrid` reports its *desired* height and keeps a too-tall options
panel tall (it hid the footer at 900x520 once). Verify the floor empirically (`ui_find` bounds of
the rows, the footer and the progress bar at 900x520 in both modes), not by arithmetic.

**H.NotifyIcon.WinUI 2.1.0 API:**
```xml
xmlns:tb="using:H.NotifyIcon"

<!-- Real markup binds Command, not Click — TrayOpenCommand/TrayAboutCommand/TrayExitCommand/
     TrayLeftClickCommand are RelayCommand/AsyncRelayCommand properties on MainWindow itself
     (constructed before InitializeComponent, see ARCHITECTURE.md's DI section), not code-behind
     event handlers. LeftClickCommand toggles the window via AppWindow.IsVisible/Hide/Activate. -->
<tb:TaskbarIcon ToolTipText="Pakko" IconSource="Assets/Square44x44Logo.ico"
                LeftClickCommand="{x:Bind TrayLeftClickCommand}">
    <tb:TaskbarIcon.ContextFlyout>   <!-- NOT ContextMenu — that's 2.4+ -->
        <MenuFlyout>
            <MenuFlyoutItem x:Uid="TrayOpenMenuItem" Command="{x:Bind TrayOpenCommand}"/>
            <MenuFlyoutItem x:Uid="TrayAboutMenuItem" Command="{x:Bind TrayAboutCommand}"/>
            <MenuFlyoutItem x:Uid="TrayExitMenuItem" Command="{x:Bind TrayExitCommand}"/>
        </MenuFlyout>
    </tb:TaskbarIcon.ContextFlyout>
</tb:TaskbarIcon>
```

---

## Localization (ResW)

All UI strings in `Strings/en-US/Resources.resw`.

XAML usage:
```xml
<Button x:Uid="ClearButton"/>
<!-- Resources.resw key: ClearButton.Content = "Clear" -->
```

C# usage:
```csharp
private static readonly ResourceLoader _res = new();
StatusMessage = _res.GetString("StatusArchiving");
```

`Archiver.Core` must never reference `ResourceLoader`.
