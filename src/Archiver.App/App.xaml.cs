using Archiver.App.Services;
using Archiver.App.ViewModels;
using Archiver.Core.Interfaces;
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
        services.AddTransient<MainViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services.GetRequiredService<ILogService>().Info("Pakko started");
        _window = new MainWindow();
        var dialogService = (DialogService)Services.GetRequiredService<IDialogService>();
        dialogService.SetWindow((Microsoft.UI.Xaml.Window)_window);
        _window.Activate();
    }

    private void OnActivated(object? sender, AppActivationArguments args)
    {
        if (args.Kind != ExtendedActivationKind.File) return;
        if (args.Data is not Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs) return;

        var paths = fileArgs.Files
            .OfType<StorageFile>()
            .Select(f => f.Path)
            .ToList();

        if (paths.Count == 0) return;

        if (_window is null)
        {
            Services.GetRequiredService<ILogService>().Info("Pakko started via file activation");
            _window = new MainWindow();
            var dialogService = (DialogService)Services.GetRequiredService<IDialogService>();
            dialogService.SetWindow((Microsoft.UI.Xaml.Window)_window);
            _window.Activate();
        }
        else
        {
            _window.Activate();
        }

        _window.ViewModel.AddPaths(paths);
    }
}
