using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Archiver.Core.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archiver.App.Core;

/// <summary>One top-level path in the pending archive-creation/extraction list, with its size and
/// CRC-32 computed in the background. Disposing it (the row was removed) stops that work.</summary>
public sealed partial class FileItem : ObservableObject, IDisposable
{
    // Caps concurrent CRC-32 reads across every FileItem, not per-instance — reading a file's
    // full content is real disk I/O, and adding many/large files at once (e.g. a folder full of
    // large files picked individually rather than as one collapsed folder row) would otherwise
    // spawn one Task.Run per file with no limit. A readonly SemaphoreSlim used purely for
    // concurrency throttling isn't the kind of mutable service state CLAUDE.md's "no static
    // mutable fields" rule targets — it holds no data, only a fixed synchronization primitive.
    private static readonly SemaphoreSlim _crc32Throttle = new(4);

    private readonly CancellationTokenSource _cts = new();

    public string FullPath { get; }
    public string Name { get; }
    public string Type { get; }

    public bool IsFolder { get; }
    public DateTime Modified { get; }
    public string ModifiedDisplay => Modified.ToString("yyyy-MM-dd HH:mm");

    // T-F198 item 2: a list row's accessible name is its item's ToString().
    public override string ToString() => Name;

    [ObservableProperty]
    public partial string Size { get; set; } = "...";

    [ObservableProperty]
    public partial long SizeBytes { get; set; } = -1;

    // Empty (not "...") for folders — unlike size, a folder has no single meaningful CRC to
    // aggregate, so LoadCrc32Async is never started for one. Crc32 is null while a file's CRC is
    // still computing or unavailable (error reading the file); never a 0-as-sentinel — an empty
    // file's CRC-32 is legitimately 0.
    [ObservableProperty]
    public partial string Crc32Display { get; set; } = string.Empty;

    [ObservableProperty]
    public partial uint? Crc32 { get; set; }

    /// <summary>Files in this item: 1 for a file, the folder's file count once <see cref="TotalsReady"/>
    /// completes (0 when it could not be measured).</summary>
    public int FileCount { get; private set; }

    /// <summary>Completes when <see cref="SizeBytes"/> and <see cref="FileCount"/> are final. Never
    /// faults: a failed or stopped walk leaves <see cref="SizeBytes"/> at -1.</summary>
    public Task TotalsReady { get; }

    /// <summary>Completes when <see cref="Crc32"/> and <see cref="Crc32Display"/> are final (at once
    /// for a folder). Never faults: an unreadable file leaves <see cref="Crc32"/> null.</summary>
    public Task Crc32Ready { get; }

    /// <summary>Creates the item, or returns null when <paramref name="path"/> cannot be read.</summary>
    public static FileItem? TryCreate(string path)
    {
        try
        {
            return new FileItem(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private FileItem(string path)
    {
        FullPath = path;
        Name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(Name))
            Name = path;

        if (Directory.Exists(path))
        {
            IsFolder = true;
            Type = DisplayText.Folder;
            Modified = Directory.GetLastWriteTime(path);
            TotalsReady = LoadFolderSizeAsync(path, _cts.Token);
            Crc32Ready = Task.CompletedTask;
        }
        else
        {
            string ext = Path.GetExtension(path).TrimStart('.');
            Type = string.IsNullOrEmpty(ext) ? DisplayText.File : ext.ToUpperInvariant();
            var fi = new FileInfo(path);
            Modified = fi.LastWriteTime;
            SizeBytes = fi.Length;
            Size = FormatSize(fi.Length);
            FileCount = 1;
            TotalsReady = Task.CompletedTask;
            Crc32Display = "...";
            Crc32Ready = LoadCrc32Async(path, _cts.Token);
        }
    }

    /// <summary>Stops the size walk and a CRC-32 read that has not started yet.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    // T-F236: the engines' walk (links not followed, an unreadable subfolder adds nothing), stopped
    // when the row is removed — adding C:\ used to walk the whole drive to the end regardless.
    private async Task LoadFolderSizeAsync(string path, CancellationToken cancellationToken)
    {
        FolderTotals? totals;
        try
        {
            totals = await Task.Run(() => FolderTotals.Measure(path, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return; // the row is gone
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            totals = null;
        }
        SizeBytes = totals?.Bytes ?? -1;
        FileCount = totals?.Files ?? 0;
        Size = totals is { } t ? FormatSize(t.Bytes) : "?";
    }

    // Async and throttled, not lazy — starts immediately for every file but never more than
    // _crc32Throttle's limit run concurrently, so queuing many/large files can't turn into an
    // unbounded disk-I/O storm. A removed item gives up its place in the queue; a read already
    // running finishes.
    private async Task LoadCrc32Async(string path, CancellationToken cancellationToken)
    {
        try
        {
            await _crc32Throttle.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return; // the row is gone
        }
        try
        {
            uint? crc = await Task.Run(() =>
            {
                try
                {
                    using FileStream stream = File.OpenRead(path);
                    return (uint?)Archiver.Core.IO.Crc32.Compute(stream);
                }
                catch { return null; }
            }, cancellationToken);
            Crc32 = crc;
            Crc32Display = crc is { } value ? $"{value:X8}" : "?";
        }
        catch (OperationCanceledException)
        {
            // the row is gone before its read started
        }
        finally
        {
            _crc32Throttle.Release();
        }
    }

    /// <summary>Formats a byte count as B/KB/MB/GB for display, in the App's language.</summary>
    public static string FormatSize(long bytes) => DisplayText.FormatSize(bytes);
}
