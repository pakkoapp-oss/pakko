# BOOTSTRAP.md — Dependency Injection and App Startup

This file defines how all services and ViewModels are wired together.
Agent must follow this exactly — do not use `new` to instantiate services in ViewModels or Views.

---

## DI Container

Use `Microsoft.Extensions.DependencyInjection` (included with Windows App SDK).

---

## App.xaml.cs — Full Wiring

```csharp
// App.xaml.cs
using Archiver.App.Services;
using Archiver.App.ViewModels;
using Archiver.Core.Interfaces;
using Archiver.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Archiver.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Core layer — no UI dependencies
        services.AddSingleton<IArchiveService, ZipArchiveService>();

        // App layer
        services.AddSingleton<IDialogService, DialogService>();
        services.AddTransient<MainViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        window.Activate();
    }
}
```

---

## MainWindow.xaml.cs — ViewModel Resolution

```csharp
// Views/MainWindow.xaml.cs
using Archiver.App.ViewModels;
using Microsoft.UI.Xaml;

namespace Archiver.App.Views;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
    }
}
```

---

## Registration Rules

| Type | Lifetime | Reason |
|------|----------|--------|
| `ZipArchiveService` | `Singleton` | Stateless, safe to reuse |
| `DialogService` | `Singleton` | Holds window reference |
| `MainViewModel` | `Transient` | Fresh state per window |

---

## Rules for Agent

- Never call `new ZipArchiveService()` outside of `ConfigureServices()`
- Never access `App.Services` from inside `Archiver.Core`
- `DialogService` must receive the `Window` reference — pass via constructor or setter after window creation:

```csharp
// After window is created in OnLaunched:
var dialogService = (DialogService)App.Services.GetRequiredService<IDialogService>();
dialogService.SetWindow(window);
```

---

## IDialogService — Full Interface

```csharp
// Archiver.App/Services/IDialogService.cs
namespace Archiver.App.Services;

public interface IDialogService
{
    Task ShowErrorAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message);
    Task<string?> PickDestinationFolderAsync();
    Task<IReadOnlyList<string>> PickFilesAsync();
}
```

## DialogService — Implementation Outline

```csharp
// Archiver.App/Services/DialogService.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Archiver.App.Services;

public sealed class DialogService : IDialogService
{
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

    public async Task<string?> PickDestinationFolderAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window!));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window!));
        var files = await picker.PickMultipleFilesAsync();
        return files?.Select(f => f.Path).ToList() ?? [];
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
}
```
