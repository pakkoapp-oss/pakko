# XAML.md — UI Structure Reference

> **Historical note:** This file originally contained the bootstrap skeleton for MainWindow.
> The UI is now fully implemented. This file describes the current actual structure.

---

## Current MainWindow.xaml Structure

```
Window
└── Grid (RowDefinitions="Auto,*,Auto,Auto,Auto,Auto,Auto" Padding="16" RowSpacing="12")
    │
    ├── [tb:TaskbarIcon] — system tray (not in grid flow, Grid.RowSpan="7")
    │
    ├── Row 0: StackPanel (Horizontal) — Add files / Add folder buttons
    │
    ├── Row 1: Grid (RowDefinitions="Auto,*") — File table
    │   ├── Row 0: Border — column header (Name / Type / Size / Modified)
    │   │           sortable buttons, Background=SubtleFillColorSecondaryBrush
    │   └── Row 1: Grid — body
    │       ├── ListView (AllowDrop, DragOver, Drop handlers)
    │       │   └── DataTemplate → Grid (4 columns) + ContextFlyout "Remove"
    │       └── StackPanel (overlay, IsHitTestVisible=False)
    │               empty-state hint, Visibility=IsFileListEmptyVisibility
    │
    ├── Row 2: Grid (3 columns) — Destination path
    │   ├── TextBlock x:Uid="DestinationLabel"
    │   ├── TextBox (IsReadOnly, bound to DestinationPath)
    │   └── Button "..."
    │
    ├── Row 3: Grid (3 columns) — Action buttons
    │   ├── Button x:Uid="ArchiveButton" (AccentButtonStyle)
    │   ├── Button x:Uid="ExtractButton"
    │   └── Button x:Uid="ClearButton"
    │
    ├── Row 4: StackPanel — Archive options
    │   ├── RadioButtons: Mode (One archive / Separate archives)
    │   ├── Grid: Name field (TextBox, disabled in SeparateArchives mode)
    │   └── StackPanel: Compression ComboBox (Fast/Normal/Best/None)
    │
    ├── Row 5: StackPanel — Shared options + checkboxes
    │   ├── StackPanel: "If file exists" ComboBox (Overwrite/Skip/Rename)
    │   ├── CheckBox x:Uid="OpenDestinationCheck"
    │   ├── CheckBox x:Uid="DeleteSourceCheck"
    │   └── CheckBox x:Uid="DeleteArchiveCheck"
    │
    └── Row 6: Grid (RowDefinitions="Auto,Auto") — Status bar
        ├── ProgressBar (Value, IsIndeterminate, Visibility=IsOperationRunning)
        └── TextBlock (StatusMessage, Opacity=0.7, FontSize=12)
```

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

**H.NotifyIcon.WinUI 2.1.0 API:**
```xml
xmlns:tb="using:H.NotifyIcon"

<tb:TaskbarIcon ToolTipText="Pakko" IconSource="Assets/Square44x44Logo.ico">
    <tb:TaskbarIcon.ContextFlyout>   <!-- NOT ContextMenu — that's 2.4+ -->
        <MenuFlyout>
            <MenuFlyoutItem Text="Open Pakko" Click="TrayOpen_Click"/>
            <MenuFlyoutSeparator/>
            <MenuFlyoutItem Text="Exit" Click="TrayExit_Click"/>
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
