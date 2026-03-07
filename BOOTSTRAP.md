# BOOTSTRAP.md — Dependency Injection and App Startup

> **Current as of v1.0.** Reflects actual implemented wiring.

---

## DI Registration

```csharp
// App.xaml.cs — ConfigureServices()
services.AddSingleton<IArchiveService, ZipArchiveService>();
services.AddSingleton<IDialogService, DialogService>();
services.AddSingleton<ILogService, LogService>();
services.AddTransient<MainViewModel>();
```

| Type | Lifetime | Reason |
|------|----------|--------|
| `ZipArchiveService` | Singleton | Stateless |
| `DialogService` | Singleton | Holds window reference |
| `LogService` | Singleton | Holds file path, lock object |
| `MainViewModel` | Transient | Fresh state per window |

---

## ViewModel Resolution

```csharp
// MainWindow.xaml.cs
public MainWindow()
{
    InitializeComponent();
    ViewModel = App.Services.GetRequiredService<MainViewModel>();
    this.AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 700));
    this.AppWindow.Title = "Pakko";
    // ... icon, tray setup
}
```

---

## Rules

- Never call `new ZipArchiveService()` outside `ConfigureServices()`
- Never access `App.Services` from inside `Archiver.Core`
- `Archiver.Core` has zero references to any app service
