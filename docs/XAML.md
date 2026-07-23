# XAML.md — UI Structure Reference

> **Historical note:** This file originally contained the bootstrap skeleton for MainWindow.
> The UI is now fully implemented. This file describes the current actual structure.

> **Last verified against `src/Archiver.App/MainWindow.xaml` directly, 2026-07-18** (full
> documentation audit) — the tree below reflects the real 8-row `Grid`, not an earlier 7-row
> draft. If you touch `MainWindow.xaml`'s row structure, re-verify this section the same way
> (read the file, don't pattern-match the old tree) per `DIAGRAMS.md`'s Ground Truth Rule, which
> applies just as much to this file.

---

## Current MainWindow.xaml Structure

```
Window
└── Grid (RowDefinitions="Auto,* MinHeight=140,Auto,Auto,Auto,Auto,Auto,Auto" Padding="16" RowSpacing="12")
    │   — 8 rows. Row 1's MinHeight lives on the RowDefinition itself, not a child control
    │     (T-F106 — a child's own MinHeight does not force a Star row to grow).
    │
    ├── [tb:TaskbarIcon] — system tray (not in grid flow)
    │
    ├── Row 0 (pending mode): Grid (Auto,Auto,*,Auto,Auto) — Add Files / Add Folder / (spacer) /
    │       Hash / About buttons. Visibility=IsPendingListVisibility.
    ├── Row 0 (browse mode): Grid (*,Auto) — only an About button, right-aligned.
    │       Visibility=IsBrowsingArchiveVisibility. Info/Close were both removed from here
    │       (design review 2026-07-13; see notes below) — do not assume they still exist.
    │
    ├── Row 1 (pending mode): Grid (RowDefinitions="Auto,*") — File table.
    │       Visibility=IsPendingListVisibility.
    │   ├── Header: Border → Grid (*,80,100,90,140) — Name/Type/Size/Crc/Modified,
    │   │       each a sortable Button (SortByCommand), Background=SubtleFillColorSecondaryBrush
    │   └── Body: Grid — ListView (AllowDrop/DragOver/Drop/DoubleTapped→PendingList_DoubleTapped)
    │       │       ItemTemplate: Grid (*,80,100,90,140) — Name/Type/Size/Crc32Display/
    │       │       ModifiedDisplay TextBlocks + ContextFlyout "Remove"
    │       └── StackPanel overlay (IsHitTestVisible=False) — empty-state hint,
    │               Visibility=IsFileListEmptyVisibility
    │
    ├── Row 1 (browse mode): Grid (RowDefinitions="Auto,Auto,*") — Archive Browser.
    │       Visibility=IsBrowsingArchiveVisibility.
    │   ├── Grid (Auto,*) — Up button (NavigateUpCommand, T-F107 — climbs past the archive root
    │   │       into the real filesystem, never exits the browser) + BreadcrumbBar
    │   ├── Header: Border → Grid (Auto,*,100,100,90,140) — icon column (T-F110) has no header
    │   │       text, then Name/Size/Packed/Crc/Modified TextBlocks (not sortable buttons here,
    │   │       unlike the pending-mode header)
    │   └── ListView (SelectionMode=Multiple, VirtualizingStackPanel, SelectionChanged +
    │           DoubleTapped→ArchiveBrowserList_DoubleTapped)
    │       ItemTemplate: Grid (Auto,*,100,100,90,140) — FontIcon(Icon)/Name/SizeDisplay/
    │       CompressedSizeDisplay/CrcDisplay/ModifiedDisplay
    │
    ├── Row 2 (shared, both modes): Grid (Auto,Auto,*,Auto) — Destination path.
    │       DestinationLabel, an Up button (NavigateDestinationUpCommand — real-filesystem
    │       parent-folder navigation, disabled at a drive root; a DIFFERENT command from Row 1
    │       browse mode's Up button despite the identical glyph — don't assume they're the same
    │       control), read-only TextBox bound to DestinationPath, "..." browse Button.
    │
    ├── Row 3 (pending mode): Grid (*,*,Auto) — Archive/Extract/Clear buttons.
    │       Visibility=IsPendingListVisibility.
    ├── Row 3 (browse mode): Grid (*,*) — Extract Selected / Extract All buttons, deliberately
    │       anchored here (not moved to Row 0) since they consume Row 2/6's destination/conflict
    │       options below them. Visibility=IsBrowsingArchiveVisibility.
    │
    ├── Row 4: TextBlock — Operation Outcome subtitle. Text=OperationOutcomeText,
    │       Visibility=OperationOutcomeVisibility (= !IsBrowsingArchive && FileItems.Count>0).
    │
    ├── Row 5: one Grid (not per-row StackPanels, so column 0's Auto width aligns across every
    │       row regardless of locale string length — see "No IsSharedSizeScope" below), 4 rows —
    │       Mode (RadioButtons: One archive / Separate archives), Name (TextBox, disabled in
    │       SeparateArchives mode), Format (ComboBox — Zip + 6 tar variants, T-F105), Compression
    │       (ComboBox; IsCompressionLevelEnabled greys it out only when plain Tar is selected).
    │
    ├── Row 6 (shared, both modes): StackPanel — "If file exists" ComboBox with 4 items
    │       (Overwrite/Skip/Rename/**Ask**, T-F06 — not 3), OpenDestinationCheck, and a single
    │       **DeleteAfterOperationCheck** (the old separate DeleteSourceCheck/DeleteArchiveCheck
    │       were consolidated into one checkbox — do not document them as two).
    │
    └── Row 7: Grid (RowDefinitions="Auto,Auto") — Status bar.
        ├── Grid (*,Auto) — ProgressBar (Value/IsIndeterminate/Visibility=IsOperationRunning) +
        │       a Cancel Button (same row, same Visibility condition — easy to miss since it
        │       wasn't in earlier drafts of this doc)
        └── TextBlock (StatusMessage, Opacity=0.7, FontSize=12, TextTrimming=CharacterEllipsis)
```

**Two distinct "Up" buttons, easy to conflate:** Row 1 browse mode's Up button
(`NavigateUpCommand`) climbs *inside* the archive/real-filesystem browse stack (T-F98/T-F107).
Row 2's Up button (`NavigateDestinationUpCommand`) walks the chosen **destination** folder up one
level via `Path.GetDirectoryName`. Both use the identical Segoe MDL2 `&#xE74A;` glyph and near-
identical markup, but they bind to different commands with different `CanExecute` gates — a future
edit to one must not assume it covers the other.

---

## WinUI 3 Constraints Learned

**`Window` is not `FrameworkElement`:**
- Do NOT use `x:Load` on direct children of `Window` — causes `FindName` CS1061
- Use `Visibility` binding instead of `x:Load`

**Empty-state overlay pattern:**
```xml
<Grid>
    <ListView AllowDrop="True" DragOver="..." Drop="..."/>
    <StackPanel IsHitTestVisible="False"
                Visibility="{x:Bind ViewModel.IsFileListEmptyVisibility, Mode=OneWay}">
        <!-- hint text -->
    </StackPanel>
</Grid>
```
`IsHitTestVisible="False"` — overlay is visible but transparent to drag/drop events.

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
(`MainWindow.xaml`'s Row 1: `<RowDefinition Height="*" MinHeight="140"/>` — tuned down from an
initial `200` in a same-day follow-up, see `DECISIONS.md`'s two T-F106 entries for the full
history). Pair this with an explicit window-size floor — `MainWindow.xaml.cs` sets
`OverlappedPresenter.PreferredMinimumWidth="900"`/`PreferredMinimumHeight="780"` (tuned down from
an initial `850`) and an initial `AppWindow.Resize(1100, 780)` — or a user can still shrink the
window enough to starve the Star row (or clip content *below* the table) even with the
`RowDefinition` fix in place. Both numbers were arrived at empirically (`ui_find` bounds-checking
every row at the enforced floor in both pending-list and Archive Browser modes), not by
arithmetic — a rough sibling-row height estimate undershot the real tuned value once already.

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
<Button x:Uid="ArchiveButton"/>
<!-- Resources.resw key: ArchiveButton.Content = "Archive" -->
```

C# usage:
```csharp
private static readonly ResourceLoader _res = new();
StatusMessage = _res.GetString("StatusArchiving");
```

`Archiver.Core` must never reference `ResourceLoader`.
