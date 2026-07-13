using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archiver.App.Models;

public sealed partial class FileItem : ObservableObject
{
    // Caps concurrent CRC-32 reads across every FileItem, not per-instance — reading a file's
    // full content is real disk I/O, and adding many/large files at once (e.g. a folder full of
    // large files picked individually rather than as one collapsed folder row) would otherwise
    // spawn one Task.Run per file with no limit. A readonly SemaphoreSlim used purely for
    // concurrency throttling isn't the kind of mutable service state CLAUDE.md's "no static
    // mutable fields" rule targets — it holds no data, only a fixed synchronization primitive.
    private static readonly SemaphoreSlim _crc32Throttle = new(4);

    public string FullPath { get; }
    public string Name { get; }
    public string Type { get; }
    public DateTime Modified { get; }
    public string ModifiedDisplay => Modified.ToString("yyyy-MM-dd HH:mm");

    [ObservableProperty]
    private string _size = "...";

    [ObservableProperty]
    private long _sizeBytes = -1;

    // Empty (not "...") for folders — unlike size, a folder has no single meaningful CRC to
    // aggregate, so LoadCrc32Async is never started for one. Crc32 is null while a file's CRC is
    // still computing or unavailable (error reading the file); never a 0-as-sentinel — an empty
    // file's CRC-32 is legitimately 0.
    [ObservableProperty]
    private string _crc32Display = string.Empty;

    [ObservableProperty]
    private uint? _crc32;

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
            Crc32Display = "...";
            _ = LoadCrc32Async(path);
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

    // Async and throttled, not lazy — starts immediately for every file (matching
    // LoadFolderSizeAsync's existing pattern) but never more than _crc32Throttle's limit run
    // concurrently, so queuing many/large files can't turn into an unbounded disk-I/O storm. No
    // cancellation if the item is later removed/cleared — same tradeoff LoadFolderSizeAsync
    // already accepts; a removed item's read still finishes and holds a throttle slot until then.
    private async Task LoadCrc32Async(string path)
    {
        await _crc32Throttle.WaitAsync();
        try
        {
            var crc = await Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    return (uint?)Archiver.Core.IO.Crc32.Compute(stream);
                }
                catch { return null; }
            });
            Crc32 = crc;
            Crc32Display = crc is { } value ? $"{value:X8}" : "?";
        }
        finally
        {
            _crc32Throttle.Release();
        }
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };
}
