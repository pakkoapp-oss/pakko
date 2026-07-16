using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Archiver.App.Models;
using Archiver.Core.Models;
using Windows.ApplicationModel.Resources;
using Windows.System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace Archiver.App.Services;

// Full implementation in T-10. Stub registered here so DI container builds.
public sealed class DialogService : IDialogService
{
    private static readonly ResourceLoader _res = ResourceLoader.GetForViewIndependentUse();

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

    // T-F94: the archive extractors call this from a thread-pool thread (ZipArchiveService's
    // Task.Run, TarProcessService's ConfigureAwait(false) chain) — ContentDialog.ShowAsync()
    // requires the calling thread to own the window's DispatcherQueue, so the dialog itself must
    // be built and shown on the UI thread via DispatcherQueue.TryEnqueue, not called directly
    // from wherever this method happens to run. See DECISIONS.md's T-F94 entry.
    public Task<bool> ShowCompressionBombConfirmAsync(CompressionBombWarning warning)
    {
        var tcs = new TaskCompletionSource<bool>();

        bool enqueued = _window!.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = _res.GetString("CompressionBombDialogTitle"),
                    Content = _res.GetString("CompressionBombDialogMessage")
                        .Replace("{0}", FileItem.FormatSize(warning.DeclaredUncompressedSize))
                        .Replace("{1}", warning.Ratio.ToString()),
                    PrimaryButtonText = "Yes",
                    CloseButtonText = "No",
                    XamlRoot = _window!.Content.XamlRoot
                };
                var result = await dialog.ShowAsync();
                tcs.SetResult(result == ContentDialogResult.Primary);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        if (!enqueued)
            tcs.SetResult(false);

        return tcs.Task;
    }

    // T-F06: same DispatcherQueue-marshaling need as ShowCompressionBombConfirmAsync above — the
    // extraction/archive services call ResolveConflictAsync from a background thread.
    public Task<ConflictDecision> ShowConflictDialogAsync(ConflictInfo conflict)
    {
        var tcs = new TaskCompletionSource<ConflictDecision>();

        bool enqueued = _window!.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var applyToAllCheck = new CheckBox
                {
                    Content = _res.GetString("ConflictDialogApplyToAllCheck"),
                    Margin = new Thickness(0, 12, 0, 0)
                };

                var panel = new StackPanel { Spacing = 8 };
                panel.Children.Add(new TextBlock
                {
                    Text = _res.GetString("ConflictDialogMessage")
                        .Replace("{0}", Path.GetFileName(conflict.ExistingPath)),
                    TextWrapping = TextWrapping.Wrap
                });
                panel.Children.Add(applyToAllCheck);

                var dialog = new ContentDialog
                {
                    Title = _res.GetString("ConflictDialogTitle"),
                    Content = panel,
                    PrimaryButtonText = _res.GetString("ConflictDialogOverwriteButton"),
                    SecondaryButtonText = _res.GetString("ConflictDialogRenameButton"),
                    CloseButtonText = _res.GetString("ConflictDialogSkipButton"),
                    DefaultButton = ContentDialogButton.Close, // Enter resolves to Skip, not Overwrite
                    XamlRoot = _window!.Content.XamlRoot
                };
                var result = await dialog.ShowAsync();

                var resolution = result switch
                {
                    ContentDialogResult.Primary => ConflictResolution.Overwrite,
                    ContentDialogResult.Secondary => ConflictResolution.Rename,
                    _ => ConflictResolution.Skip
                };
                tcs.SetResult(new ConflictDecision
                {
                    Resolution = resolution,
                    ApplyToAll = applyToAllCheck.IsChecked == true
                });
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        if (!enqueued)
            tcs.SetResult(new ConflictDecision { Resolution = ConflictResolution.Skip });

        return tcs.Task;
    }

    // T-F97: opens an Archive Browser preview file with the OS's default handler for its type.
    // Process.Start(UseShellExecute=true), not Launcher.LaunchFileAsync/StorageFile — confirmed
    // on-device that the WinRT Storage broker rejects an arbitrary %TEMP% path from this app's
    // full-trust packaged identity even though classic File I/O (which created the file) has no
    // trouble with the same path. Process.Start with shell execution is the same mechanism this
    // codebase already uses for "open destination folder" (ExtractionRouter/TarSandboxedService)
    // and needs no WinRT capability at all.
    public Task<bool> OpenFileWithDefaultAppAsync(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
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

    public async Task ShowFileHashAsync()
    {
        var files = await PickFilesAsync();
        if (files.Count == 0)
            return;

        var panel = new StackPanel { Spacing = 12 };

        foreach (var path in files)
        {
            var itemPanel = new StackPanel { Spacing = 2 };
            itemPanel.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path),
                FontWeight = FontWeights.SemiBold
            });

            string hashText;
            try
            {
                await using var stream = File.OpenRead(path);
                var hash = await SHA256.HashDataAsync(stream);
                hashText = Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                hashText = $"Error: {ex.Message}";
            }

            itemPanel.Children.Add(new TextBlock
            {
                Text = hashText,
                FontFamily = new FontFamily("Consolas"),
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85
            });
            panel.Children.Add(itemPanel);
        }

        var dialog = new ContentDialog
        {
            Title = "SHA-256",
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

    public async Task ShowAboutAsync()
    {
        string version;
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
        catch
        {
            version = "dev";
        }

        var githubUrl = _res.GetString("AboutGitHubUrl");
        var privacyUrl = _res.GetString("AboutPrivacyUrl");
        var kofiUrl = _res.GetString("AboutKofiUrl");

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Pakko  v{version}",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Lightweight native Windows archiver",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "License: Apache 2.0",
            Opacity = 0.7
        });

        var githubBtn = new HyperlinkButton { Content = "GitHub", Padding = new Thickness(0) };
        githubBtn.Click += async (_, _) => await Launcher.LaunchUriAsync(new Uri(githubUrl));

        var privacyBtn = new HyperlinkButton { Content = "Privacy Policy", Padding = new Thickness(0) };
        privacyBtn.Click += async (_, _) => await Launcher.LaunchUriAsync(new Uri(privacyUrl));

        // T-F93: same visual weight and style as GitHub/Privacy Policy, deliberately — a donate
        // link that stands out more than the other links would read as a nag, not a small link.
        var kofiBtn = new HyperlinkButton { Content = "Ko-fi", Padding = new Thickness(0) };
        kofiBtn.Click += async (_, _) => await Launcher.LaunchUriAsync(new Uri(kofiUrl));

        var linksPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        linksPanel.Children.Add(githubBtn);
        linksPanel.Children.Add(privacyBtn);
        linksPanel.Children.Add(kofiBtn);
        panel.Children.Add(linksPanel);

        var dialog = new ContentDialog
        {
            Title = "About Pakko",
            Content = panel,
            CloseButtonText = "Close",
            XamlRoot = _window!.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
