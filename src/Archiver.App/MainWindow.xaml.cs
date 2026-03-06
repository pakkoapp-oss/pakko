using Archiver.App.Models;
using Archiver.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Archiver.App;

public sealed partial class MainWindow : Window
{
    internal MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        this.AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 600));
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add to list";
        e.Handled = true;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.Select(i => i.Path).ToList();
            ViewModel.AddPaths(paths);
        }
    }

    private void DropZone_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        ViewModel.BrowseFilesCommand.Execute(null);
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.DataContext is FileItem fileItem)
            ViewModel.RemovePath(fileItem.FullPath);
    }
}
