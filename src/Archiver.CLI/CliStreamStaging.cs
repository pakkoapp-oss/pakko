namespace Archiver.CLI;

/// <summary>
/// Stages stdin to a private temp file (-si) and stages a command's output to a private temp
/// folder for streaming to stdout (-so). Archiver.Core's public API is untouched by T-F116:
/// ZipArchive needs a seekable file to read its central directory, ArchiveAsync already writes a
/// real temp file before renaming into place, and TarSandboxedService's whole-archive pre-scan
/// (T-F49) needs a real file to scan before extraction runs — none of that can operate on a raw
/// stdin pipe mid-stream, so this buffers at the CLI boundary instead of touching Core.
/// </summary>
public static class CliStreamStaging
{
    /// <summary>The staged archive's file name inside its <see cref="CliStagingFolder"/>.</summary>
    public const string StdinFileName = "stdin.bin";

    /// <summary>Root of the -si staging folders.</summary>
    public static string StdinRoot { get; } = Path.Combine(Path.GetTempPath(), "Archiver.CLI.Stdin");

    /// <summary>Root of the -so staging folders.</summary>
    public static string StdoutRoot { get; } = Path.Combine(Path.GetTempPath(), "Archiver.CLI.Stdout");

    /// <summary>Copies <paramref name="source"/> into a new folder under <paramref name="root"/>
    /// as <see cref="StdinFileName"/>. The caller owns (and disposes) the returned folder.</summary>
    public static async Task<CliStagingFolder> StageStdinAsync(string root, Stream source, CancellationToken cancellationToken)
    {
        CliStagingFolder folder = CliStagingFolder.Create(root);
        try
        {
            string filePath = Path.Combine(folder.Path, StdinFileName);
            await using (FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await source.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }
            return folder;
        }
        catch
        {
            folder.Dispose();
            throw;
        }
    }

    /// <summary>Sweeps both staging roots, checking owners against the running processes.</summary>
    public static void SweepAbandoned()
    {
        SweepAbandoned(StdinRoot, IsOwnerAlive);
        SweepAbandoned(StdoutRoot, IsOwnerAlive);
    }

    /// <summary>Deletes the staging folders under <paramref name="root"/> whose owning process
    /// is gone (<paramref name="isOwnerAlive"/> gets the PID from the folder name and the
    /// folder's creation time). Only <c>&lt;pid&gt;-&lt;32 hex&gt;</c> names are touched.</summary>
    public static void SweepAbandoned(string root, Func<int, DateTime, bool> isOwnerAlive)
    {
        DirectoryInfo[] folders;
        try
        {
            DirectoryInfo rootInfo = new(root);
            if (!rootInfo.Exists)
                return;
            folders = rootInfo.GetDirectories();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Best-effort: a sweep that cannot run is retried by the next pakko.
        }

        foreach (DirectoryInfo folder in folders)
        {
            if (TryGetOwnerProcessId(folder.Name) is not { } processId
                || isOwnerAlive(processId, folder.CreationTimeUtc))
                continue;
            try
            {
                folder.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort: still in use or locked; the next run tries again.
            }
        }
    }

    // "<pid>-<guid:N>", exactly what CliStagingFolder.Create makes; anything else is not ours.
    private static int? TryGetOwnerProcessId(string name)
    {
        int dash = name.IndexOf('-');
        if (dash <= 0 || !Guid.TryParseExact(name.AsSpan(dash + 1), "N", out _))
            return null;
        return int.TryParse(name.AsSpan(0, dash), System.Globalization.NumberStyles.None, null, out int processId)
            ? processId
            : null;
    }

    /// <summary>Whether the process that created a staging folder at
    /// <paramref name="folderCreatedUtc"/> is still running. A process that started after the
    /// folder was made reuses the PID and is not its owner. When it cannot be told (e.g. another
    /// user's process), the folder is kept.</summary>
    public static bool IsOwnerAlive(int processId, DateTime folderCreatedUtc)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime() <= folderCreatedUtc;
        }
        catch (ArgumentException)
        {
            return false; // no process with that id
        }
        catch (InvalidOperationException)
        {
            return false; // exited while being looked at
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return true;
        }
    }

    /// <summary>
    /// Streams the single file found under <paramref name="stagingDir"/> to stdout. Returns an
    /// error message (never throws) if the operation didn't resolve to exactly one output file,
    /// or if the downstream reader closed its end of the pipe early — both are real, expected
    /// failure modes for a pipeline, not exceptional conditions.
    /// </summary>
    public static async Task<string?> StreamSingleFileToStdoutAsync(string stagingDir, CancellationToken cancellationToken)
    {
        await using Stream stdout = Console.OpenStandardOutput();
        return await StreamSingleFileAsync(stagingDir, stdout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Same as <see cref="StreamSingleFileToStdoutAsync"/> but writes to an injected stream —
    /// split out so the broken-pipe/wrong-file-count failure paths are unit-testable with a fake
    /// throwing stream, instead of relying on racy real-OS-pipe timing in a subprocess test.
    /// </summary>
    public static async Task<string?> StreamSingleFileAsync(string stagingDir, Stream destination, CancellationToken cancellationToken)
    {
        string[] files = Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories);
        if (files.Length != 1)
            return $"-so requires the operation to resolve to exactly one output file, found {files.Length}";

        try
        {
            await using FileStream fileStream = new(files[0], FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
            await fileStream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (IOException)
        {
            // Downstream reader closed its end of the pipe (e.g. `pakko a -so ... | head`).
            return "downstream reader closed the pipe before all output was written";
        }
    }
}
