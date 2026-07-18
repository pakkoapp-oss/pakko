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

        // T-F51: eager, synchronous registry read — registered before every consumer below so
        // ActivatorUtilities can inject it into their optional GroupPolicyOptions? ctor params.
        services.AddSingleton(GroupPolicyService.Load());
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

    private void HandleActivation(AppActivationArguments args, string defaultLogMessage)
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
                // T-F106: deferred via ActivationGate — EnsureWindow's Activate() returns before
                // RootGrid's first Loaded/layout pass, and mutating ViewModel state before that
                // point left ListView rows permanently blank (see DeferredActionGate's doc comment).
                var decision = FileActivationRouter.Decide(paths);
                var window = _window!;
                if (decision.Mode == FileActivationMode.Browse)
                    window.ActivationGate.RunOrDefer(() => _ = EnterBrowseSafelyAsync(window, decision.BrowsePath!));
                else
                    window.ActivationGate.RunOrDefer(() => window.ViewModel.AddPaths(paths));
                break;

            case ExtendedActivationKind.Protocol:
                if (args.Data is not Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protoArgs) { EnsureWindow(defaultLogMessage); return; }
                EnsureWindow("Pakko started via protocol activation");
                var protoWindow = _window!;

                // T-F03: pakko://browse skips the pending-list/extract-options view and enters the
                // Archive Browser (T-F05) directly, the same destination FileActivationRouter
                // already routes a double-clicked single archive to (T-F100). pakko://extract and
                // pakko://archive are unaffected — unchanged AddPathsFromProtocolUri path below.
                if (ProtocolActivationRouter.TryGetBrowsePath(protoArgs.Uri.AbsoluteUri, out var browsePath))
                    protoWindow.ActivationGate.RunOrDefer(() => _ = EnterBrowseSafelyAsync(protoWindow, browsePath!));
                else
                    protoWindow.ActivationGate.RunOrDefer(() => protoWindow.ViewModel.AddPathsFromProtocolUri(protoArgs.Uri.AbsoluteUri));
                break;

            default:
                EnsureWindow(defaultLogMessage);
                break;
        }
    }

    // T-F106: deferred through ActivationGate, EnterBrowseModeAsync now runs un-awaited (no caller
    // observes its Task). MainViewModel.EnterBrowseModeAsync itself already catches and recovers
    // from any exception (resets IsBrowsingArchive, shows an error dialog — added during this same
    // fix's code-review pass, since an uncaught exception there used to crash loudly but would
    // otherwise now leave the ViewModel stuck in a blank browse view with no visible error). This
    // is a last-resort net in case that guarantee is ever violated by a future change.
    private static async Task EnterBrowseSafelyAsync(MainWindow window, string path)
    {
        try
        {
            await window.ViewModel.EnterBrowseModeAsync(path);
        }
        catch (Exception ex)
        {
            Services.GetRequiredService<ILogService>().Error("Deferred EnterBrowseModeAsync failed", ex);
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
