# XAML.md — UI Skeleton

Agent must use this as the base for `MainWindow.xaml`.
Do not redesign the layout — implement this structure exactly, then style as needed.

---

## MainWindow.xaml — Full Skeleton

```xml
<!-- Views/MainWindow.xaml -->
<Window
    x:Class="Archiver.App.Views.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:viewmodels="using:Archiver.App.ViewModels"
    Title="Archiver">

    <Grid RowDefinitions="Auto,*,Auto,Auto" Padding="16" RowSpacing="12">

        <!-- Row 0: Drop Zone -->
        <Border
            Grid.Row="0"
            Height="120"
            BorderBrush="{ThemeResource ControlStrokeColorDefaultBrush}"
            BorderThickness="2"
            CornerRadius="8"
            AllowDrop="True"
            DragOver="DropZone_DragOver"
            Drop="DropZone_Drop"
            Background="{ThemeResource CardBackgroundFillColorDefaultBrush}">
            <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="4">
                <FontIcon Glyph="&#xE8B7;" FontSize="28" Opacity="0.6"/>
                <TextBlock
                    Text="Drop files or folders here"
                    HorizontalAlignment="Center"
                    Opacity="0.6"/>
                <HyperlinkButton
                    Content="Browse files"
                    HorizontalAlignment="Center"
                    Command="{x:Bind ViewModel.BrowseFilesCommand}"/>
            </StackPanel>
        </Border>

        <!-- Row 1: Selected Files List -->
        <ListView
            Grid.Row="1"
            ItemsSource="{x:Bind ViewModel.SelectedPaths, Mode=OneWay}"
            SelectionMode="None"
            MinHeight="80">
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="x:String">
                    <TextBlock
                        Text="{x:Bind}"
                        TextTrimming="CharacterEllipsis"
                        ToolTipService.ToolTip="{x:Bind}"/>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>

        <!-- Row 2: Action Buttons + Options -->
        <Grid Grid.Row="2" ColumnDefinitions="*,*,Auto">

            <Button
                Grid.Column="0"
                Content="Archive"
                HorizontalAlignment="Stretch"
                Command="{x:Bind ViewModel.ArchiveCommand}"
                IsEnabled="{x:Bind ViewModel.IsBusy, Mode=OneWay, Converter={StaticResource InverseBoolConverter}}"
                Style="{StaticResource AccentButtonStyle}"/>

            <Button
                Grid.Column="1"
                Content="Extract"
                Margin="8,0,0,0"
                HorizontalAlignment="Stretch"
                Command="{x:Bind ViewModel.ExtractCommand}"
                IsEnabled="{x:Bind ViewModel.IsBusy, Mode=OneWay, Converter={StaticResource InverseBoolConverter}}"/>

            <Button
                Grid.Column="2"
                Content="Clear"
                Margin="8,0,0,0"
                Command="{x:Bind ViewModel.ClearCommand}"/>
        </Grid>

        <!-- Row 3: Status Bar -->
        <Grid Grid.Row="3" RowDefinitions="Auto,Auto" RowSpacing="4">
            <ProgressBar
                Grid.Row="0"
                Value="{x:Bind ViewModel.Progress, Mode=OneWay}"
                Maximum="100"
                Visibility="{x:Bind ViewModel.IsBusy, Mode=OneWay}"/>
            <TextBlock
                Grid.Row="1"
                Text="{x:Bind ViewModel.StatusMessage, Mode=OneWay}"
                Opacity="0.7"
                FontSize="12"/>
        </Grid>

    </Grid>
</Window>
```

---

## MainWindow.xaml.cs — Code-Behind

```csharp
// Views/MainWindow.xaml.cs
namespace Archiver.App.Views;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add to list";
        e.Handled = true;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.Select(i => i.Path).ToList();
            ViewModel.AddPaths(paths);
        }
    }
}
```

---

## App.xaml — Resources

Add this to `App.xaml` inside `Application.Resources`:

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls"/>
        </ResourceDictionary.MergedDictionaries>

        <!-- Converter: bool → Visibility inverse -->
        <converters:InverseBoolConverter x:Key="InverseBoolConverter"
            xmlns:converters="using:Archiver.App.Converters"/>
    </ResourceDictionary>
</Application.Resources>
```

---

## InverseBoolConverter

```csharp
// Converters/InverseBoolConverter.cs
using Microsoft.UI.Xaml.Data;

namespace Archiver.App.Converters;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : false;
}
```

---

## ViewModel — Additional Methods Required by XAML

```csharp
// Add to MainViewModel.cs

[RelayCommand]
private void AddPaths(IEnumerable<string> paths)
{
    foreach (var path in paths)
        if (!SelectedPaths.Contains(path))
            SelectedPaths.Add(path);
}

[RelayCommand]
private void Clear() => SelectedPaths.Clear();

[RelayCommand]
private async Task BrowseFilesAsync()
{
    var paths = await _dialogService.PickFilesAsync();
    AddPaths(paths);
}
```

---

## Layout Notes for Agent

- Do not add settings panels, tabs, or menus in v1.0
- Post-action options (open folder, delete source) are configured via `ArchiveOptions` / `ExtractOptions` — show them as `CheckBox` inside the Archive/Extract dialogs, not in the main window
- Window minimum size: `MinWidth="480"` `MinHeight="400"`
- Theme: respect system theme (`RequestedTheme` not set = auto)
