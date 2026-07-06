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
        AppInstance.GetCurrent().Activated += OnActivated;
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IArchiveService, ZipArchiveService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ITarService, TarProcessService>();
        services.AddSingleton<TarCapabilities>(sp =>
            sp.GetRequiredService<ITarService>().DetectCapabilitiesAsync().GetAwaiter().GetResult());
        services.AddTransient<MainViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // T-F83: AppInstance.Activated only fires for activations redirected to an
        // already-running instance — the initial cold-start activation (including File/Protocol
        // kinds) is never delivered as an event and must be pulled explicitly here. Without this,
        // a cold "Extract…"/double-click launch fell through to a blank window, silently ignoring
        // the files it was invoked with. See DECISIONS.md.
        HandleActivation(AppInstance.GetCurrent().GetActivatedEventArgs(), "Pakko started");
    }

    private void OnActivated(object? sender, AppActivationArguments args) =>
        HandleActivation(args, "Pakko started via redirected activation");

    private void HandleActivation(AppActivationArguments args, string defaultLogMessage)
    {
        switch (args.Kind)
        {
            case ExtendedActivationKind.File:
                if (args.Data is not Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs) { EnsureWindow(defaultLogMessage); return; }
                var paths = fileArgs.Files.OfType<StorageFile>().Select(f => f.Path).ToList();
                if (paths.Count == 0) { EnsureWindow(defaultLogMessage); return; }
                EnsureWindow("Pakko started via file activation");
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
