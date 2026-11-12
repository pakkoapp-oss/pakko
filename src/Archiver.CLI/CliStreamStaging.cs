using System.Runtime.InteropServices;
using Archiver.Core.IO;
using Microsoft.Win32.SafeHandles;

namespace Archiver.CLI;

/// <summary>
/// Stages stdin to a private temp file (-si) and stages a command's output to a private temp
/// folder for streaming to stdout (-so). Archiver.Core's public API is untouched by T-F116:
/// ZipArchive needs a seekable file to read its central directory, ArchiveAsync already writes a
/// real temp file before renaming into place, and TarSandboxedService's whole-archive pre-scan
/// (T-F49) needs a real file to scan before extraction runs — none of that can operate on a raw
/// stdin pipe mid-stream, so this buffers at the CLI boundary instead of touching Core.
/// </summary>
public static partial class CliStreamStaging
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
        var folder = CliStagingFolder.Create(root);
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

    private static readonly TimeSpan UnownedMaxAge = TimeSpan.FromDays(1);

    /// <summary>T-F511: removes, through <see cref="TempOwner"/>, the staging folders of runs that no
    /// longer run; v1.10's <c>&lt;pid&gt;-&lt;guid&gt;</c> folders included.</summary>
    public static void SweepAbandoned()
    {
        TempOwner.SweepStaleInTemp(StdinRoot, "", UnownedMaxAge);
        TempOwner.SweepStaleInTemp(StdoutRoot, "", UnownedMaxAge);
    }

    /// <summary>
    /// Streams the single file found under <paramref name="stagingDir"/> to stdout. Returns an
    /// error message (never throws) if the operation didn't resolve to exactly one output file,
    /// or if the downstream reader closed its end of the pipe early — both are real, expected
    /// failure modes for a pipeline, not exceptional conditions.
    /// </summary>
    public static async Task<string?> StreamSingleFileToStdoutAsync(string stagingDir, CancellationToken cancellationToken)
    {
        // T-F419: Console's own stream drops a closed pipe's error (ERROR_NO_DATA), so a reader
        // that stopped early read as a delivered archive - and -sdel then deleted the sources. A
        // FileStream over the same handle reports it.
        using var handle = new SafeFileHandle(GetStdHandle(StdOutputHandle), ownsHandle: false);
        await using Stream stdout = handle.IsInvalid ? Console.OpenStandardOutput() : new FileStream(handle, FileAccess.Write, bufferSize: 0);
        return await StreamSingleFileAsync(stagingDir, stdout, cancellationToken).ConfigureAwait(false);
    }

    private const int StdOutputHandle = -11;

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int standardHandle);

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
