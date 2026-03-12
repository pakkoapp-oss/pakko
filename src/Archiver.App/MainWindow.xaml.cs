using System.Windows.Input;
using Archiver.App.Models;
using Archiver.App.Services;
using Archiver.App.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Archiver.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public ICommand TrayOpenCommand { get; }
    public ICommand TrayAboutCommand { get; }
    public ICommand TrayExitCommand { get; }
    public ICommand TrayLeftClickCommand { get; }


    public MainWindow()
    {
        TrayOpenCommand = new RelayCommand(() =>
        {
            this.Activate();
            
        });
        TrayAboutCommand = new AsyncRelayCommand(async () =>
        {
            this.Activate();
            await App.Services.GetRequiredService<IDialogService>().ShowAboutAsync();
        });
        TrayExitCommand = new RelayCommand(() => Application.Current.Exit());
        TrayLeftClickCommand = new RelayCommand(() =>
        {
            if (this.AppWindow.IsVisible)
                this.AppWindow.Hide();
            else
                this.Activate();
        });

        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        this.AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 700));
        this.AppWindow.Title = "Pakko";

        this.AppWindow.SetIcon("Assets/Square44x44Logo.ico");

        this.Activated += OnFirstActivated;
        this.Closed += (_, _) => TrayIcon.Dispose();
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        this.Activated -= OnFirstActivated;
        TrayIcon.XamlRoot = Content.XamlRoot;
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add to list";
        e.Handled = true;
    }

    private async void FileList_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = new List<string>();
            foreach (var item in items)
            {
                var path = item switch
                {
                    Windows.Storage.StorageFile file => file.Path,
                    Windows.Storage.StorageFolder folder => folder.Path,
                    _ => item.Path
                };
                System.Diagnostics.Debug.WriteLine($"[Drop] name={item.Name} path={path}");
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }
            ViewModel.AddPaths(paths);
        }
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is FileItem fileItem)
            ViewModel.RemovePath(fileItem.FullPath);
    }
}
