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
    public static async Task<string> StageStdinAsync(CancellationToken cancellationToken)
    {
        string dir = Path.Combine(Path.GetTempPath(), "Archiver.CLI.Stdin", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string filePath = Path.Combine(dir, "stdin.bin");

        await using (FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
        await using (Stream stdin = Console.OpenStandardInput())
        {
            await stdin.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }

        return filePath;
    }

    public static void CleanupStagedStdin(string stagedFilePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(stagedFilePath);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup — a leaked %TEMP% file is not worth failing the command over.
        }
    }

    public static string CreateOutputStagingDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "Archiver.CLI.Stdout", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void CleanupOutputStagingDirectory(string stagingDir)
    {
        try
        {
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
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
