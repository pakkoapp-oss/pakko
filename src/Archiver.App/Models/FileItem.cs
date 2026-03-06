using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archiver.App.Models;

public sealed partial class FileItem : ObservableObject
{
    public string FullPath { get; }
    public string Name { get; }
    public string Type { get; }
    public DateTime Modified { get; }
    public string ModifiedDisplay => Modified.ToString("yyyy-MM-dd HH:mm");

    [ObservableProperty]
    private string _size = "...";

    [ObservableProperty]
    private long _sizeBytes = -1;

    public FileItem(string path)
    {
        FullPath = path;
        Name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(Name))
            Name = path;

        if (Directory.Exists(path))
        {
            Type = "Folder";
            Modified = Directory.GetLastWriteTime(path);
            _ = LoadFolderSizeAsync(path);
        }
        else
        {
            var ext = Path.GetExtension(path).TrimStart('.');
            Type = string.IsNullOrEmpty(ext) ? "File" : ext.ToUpperInvariant();
            var fi = new FileInfo(path);
            Modified = fi.LastWriteTime;
            SizeBytes = fi.Length;
            Size = FormatSize(fi.Length);
        }
    }

    private async Task LoadFolderSizeAsync(string path)
    {
        var bytes = await Task.Run(() =>
        {
            try
            {
                long total = 0;
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    try { total += new FileInfo(f).Length; } catch { }
                return total;
            }
            catch { return -1L; }
        });
        SizeBytes = bytes;
        Size = bytes >= 0 ? FormatSize(bytes) : "?";
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };
}
