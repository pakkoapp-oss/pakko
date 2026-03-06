using System.IO;
using Archiver.Core.Models;
using Windows.ApplicationModel.Resources;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Archiver.App.Services;

// Full implementation in T-10. Stub registered here so DI container builds.
public sealed class DialogService : IDialogService
{
    private static readonly ResourceLoader _res = new();

    private Window? _window;

    public void SetWindow(Window window) => _window = window;

    public async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = _window!.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            XamlRoot = _window!.Content.XamlRoot
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public async Task<string?> PickDestinationFolderAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        var files = await picker.PickMultipleFilesAsync();
        return files?.Select(f => f.Path).ToList() ?? [];
    }

    public async Task<IReadOnlyList<string>> PickFoldersAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        var folder = await picker.PickSingleFolderAsync();
        return folder is null ? [] : [folder.Path];
    }

    public async Task ShowOperationSummaryAsync(string operationName, ArchiveResult result)
    {
        if (result.Errors.Count == 0 && result.SkippedFiles.Count == 0)
            return;

        var panel = new StackPanel { Spacing = 8 };

        if (result.Errors.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"\u2717 {_res.GetString("ErrorSectionHeader")} ({result.Errors.Count})",
                FontWeight = FontWeights.SemiBold
            });

            foreach (var error in result.Errors)
            {
                var itemPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 4) };
                itemPanel.Children.Add(new TextBlock
                {
                    Text = Path.GetFileName(error.SourcePath),
                    FontWeight = FontWeights.SemiBold
                });
                itemPanel.Children.Add(new TextBlock
                {
                    Text = error.Message,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7
                });
                panel.Children.Add(itemPanel);
            }
        }

        if (result.SkippedFiles.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"\u2298 {_res.GetString("SkippedSectionHeader")} ({result.SkippedFiles.Count})",
                FontWeight = FontWeights.SemiBold
            });

            foreach (var skipped in result.SkippedFiles)
            {
                var itemPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 4) };
                itemPanel.Children.Add(new TextBlock
                {
                    Text = Path.GetFileName(skipped.Path),
                    FontWeight = FontWeights.SemiBold
                });
                itemPanel.Children.Add(new TextBlock
                {
                    Text = skipped.Reason,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7
                });
                panel.Children.Add(itemPanel);
            }
        }

        var dialog = new ContentDialog
        {
            Title = _res.GetString("ErrorDialogTitle"),
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 400,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            CloseButtonText = "OK",
            XamlRoot = _window!.Content.XamlRoot
        };

        await dialog.ShowAsync();
    }
}
