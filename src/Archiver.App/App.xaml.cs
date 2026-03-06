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

        services.AddSingleton<IArchiveService, ZipArchiveService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddTransient<MainViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        var dialogService = (DialogService)Services.GetRequiredService<IDialogService>();
        dialogService.SetWindow(window);
        window.Activate();
    }
}
