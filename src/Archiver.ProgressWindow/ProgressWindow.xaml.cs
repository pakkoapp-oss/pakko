using Archiver.ProgressWindow.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Archiver.ProgressWindow;

public sealed partial class ProgressWindow : Microsoft.UI.Xaml.Window
{
    public ProgressViewModel ViewModel { get; }

    public ProgressWindow(string pipeName, string operationTitle)
    {
        InitializeComponent();

        this.AppWindow.Title = operationTitle;
        this.AppWindow.Resize(new SizeInt32(416, 160));

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        this.AppWindow.SetPresenter(presenter);

        ViewModel = new ProgressViewModel(pipeName);

        // Wire lifecycle callbacks — window API calls, not business logic.
        ViewModel.CloseWindow = () => this.Close();
        ViewModel.ShowErrorDialog = async msg =>
        {
            var dlg = new ContentDialog
            {
                Title = "Operation Failed",
                Content = msg,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await dlg.ShowAsync();
        };

        _ = ViewModel.InitAsync();
    }
}
