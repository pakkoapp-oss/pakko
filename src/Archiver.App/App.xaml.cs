using Archiver.App.Core;
using Archiver.App.Services;
using Archiver.App.ViewModels;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Storage;

namespace Archiver.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IArchiveService, ZipArchiveService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ITarService, TarSandboxedService>();
        services.AddSingleton<TarCapabilities>(sp =>
            sp.GetRequiredService<ITarService>().DetectCapabilitiesAsync().GetAwaiter().GetResult());
        services.AddSingleton<IExtractionRouter, ExtractionRouter>();
        services.AddSingleton<IArchiveListingRouter, ArchiveListingRouter>();
        services.AddSingleton<IArchiveCreationRouter, ArchiveCreationRouter>();
        services.AddTransient<MainViewModel>();

        var provider = services.BuildServiceProvider();

        // T-F48: force tar.exe capability detection now — a factory-registered singleton only
        // runs on first resolution, and nothing else currently injects TarCapabilities.
        provider.GetRequiredService<TarCapabilities>();

        return provider;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // T-F83: this activation is never delivered as an event on cold start (there is no
        // AppInstance.Activated subscriber — see DECISIONS.md's T-F88 entry: Pakko is
        // deliberately multi-instance, one process per launch, matching 7-Zip/WinRAR/NanaZip) —
        // it must be pulled explicitly here, including File/Protocol kinds. Without this, a cold
        // "Extract…"/double-click launch fell through to a blank window, silently ignoring the
        // files it was invoked with.
        HandleActivation(AppInstance.GetCurrent().GetActivatedEventArgs(), "Pakko started");
    }

    private async void HandleActivation(AppActivationArguments args, string defaultLogMessage)
    {
        switch (args.Kind)
        {
            case ExtendedActivationKind.File:
                if (args.Data is not Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs) { EnsureWindow(defaultLogMessage); return; }
                var paths = fileArgs.Files.OfType<StorageFile>().Select(f => f.Path).ToList();
                if (paths.Count == 0) { EnsureWindow(defaultLogMessage); return; }
                EnsureWindow("Pakko started via file activation");

                // T-F100: a single recognized archive enters the browser (T-F05) instead of the
                // archive-creation list; multi-file activation keeps the existing AddPaths behavior
                // (browsing only makes sense for one archive at a time).
                var decision = FileActivationRouter.Decide(paths);
                if (decision.Mode == FileActivationMode.Browse)
                    await _window!.ViewModel.EnterBrowseModeAsync(decision.BrowsePath!);
                else
                    _window!.ViewModel.AddPaths(paths);
                break;

            case ExtendedActivationKind.Protocol:
                if (args.Data is not Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protoArgs) { EnsureWindow(defaultLogMessage); return; }
                EnsureWindow("Pakko started via protocol activation");
                _window!.ViewModel.AddPathsFromProtocolUri(protoArgs.Uri.AbsoluteUri);
                break;

            default:
                EnsureWindow(defaultLogMessage);
                break;
        }
    }

    private void EnsureWindow(string logMessage)
    {
        if (_window is null)
        {
            Services.GetRequiredService<ILogService>().Info(logMessage);
            _window = new MainWindow();
            var dialogService = (DialogService)Services.GetRequiredService<IDialogService>();
            dialogService.SetWindow((Microsoft.UI.Xaml.Window)_window);
            _window.Activate();
        }
        else
        {
            _window.Activate();
        }
    }
}
