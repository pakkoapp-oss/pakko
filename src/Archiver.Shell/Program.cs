using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Shell;

var command = ShellArgumentParser.Parse(args);

if (command.Type == CommandType.Invalid)
{
    // WinExe: no console window shown. Exit silently.
    Environment.Exit(1);
    return;
}

switch (command.Type)
{
    case CommandType.OpenUiExtract:
        LaunchOpenUi("extract", command.Files);
        break;

    case CommandType.OpenUiArchive:
        LaunchOpenUi("archive", command.Files);
        break;

    case CommandType.ExtractHere:
        await RunExtractHereAsync(command.Files).ConfigureAwait(false);
        break;

    case CommandType.ExtractFolder:
        await RunExtractFolderAsync(command.Files).ConfigureAwait(false);
        break;

    case CommandType.Archive:
        await RunArchiveAsync(command.Files).ConfigureAwait(false);
        break;
}

// -------------------------------------------------------------------------
// Open-UI flow: encode files as a base64 JSON array and launch Archiver.App
// via the pakko:// URI scheme. Archiver.Shell exits immediately; the app
// takes over the user interaction.
// -------------------------------------------------------------------------
static void LaunchOpenUi(string operation, IReadOnlyList<string> files)
{
    // TODO T-F56: The pakko:// URI scheme must be registered in
    // Package.appxmanifest before this Process.Start call will succeed.
    // Registration is handled as part of T-F56 (Protocol Activation).
    var base64 = Convert.ToBase64String(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(files)));
    var uri = $"pakko://{operation}?files={base64}";

    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
}

// -------------------------------------------------------------------------
// --extract-here: extract each archive to its own sibling directory.
// Uses T-14 smart folder logic in ZipArchiveService (SeparateFolders mode):
// single root folder → strips prefix; multiple roots → creates wrapper folder.
// -------------------------------------------------------------------------
static async Task RunExtractHereAsync(IReadOnlyList<string> archivePaths)
{
    var service = new ZipArchiveService();

    foreach (var archivePath in archivePaths)
    {
        var destFolder = Path.GetDirectoryName(archivePath) ?? ".";
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destFolder,
            Mode = ExtractMode.SeparateFolders,
            OnConflict = ConflictBehavior.Skip,
        };

        string title = $"Extracting: {Path.GetFileName(archivePath)}";
        await RunWithProgressWindowAsync(title,
            (progress, ct) => service.ExtractAsync(options, progress, ct))
            .ConfigureAwait(false);
    }
}

// -------------------------------------------------------------------------
// --extract-folder: always extract into an explicit <archive_name>\ subfolder,
// regardless of the archive's internal structure.
// -------------------------------------------------------------------------
static async Task RunExtractFolderAsync(IReadOnlyList<string> archivePaths)
{
    var service = new ZipArchiveService();

    foreach (var archivePath in archivePaths)
    {
        var archiveDir = Path.GetDirectoryName(archivePath) ?? ".";
        var folderName = Path.GetFileNameWithoutExtension(archivePath);
        var destFolder = Path.Combine(archiveDir, folderName);
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destFolder,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip,
        };

        string title = $"Extracting: {Path.GetFileName(archivePath)}";
        await RunWithProgressWindowAsync(title,
            (progress, ct) => service.ExtractAsync(options, progress, ct))
            .ConfigureAwait(false);
    }
}

// -------------------------------------------------------------------------
// --archive: pack all source paths into a single ZIP placed next to the first
// item, named after the first item.
// -------------------------------------------------------------------------
static async Task RunArchiveAsync(IReadOnlyList<string> sourcePaths)
{
    var firstPath = sourcePaths[0];
    var destFolder = Path.GetDirectoryName(firstPath) ?? ".";
    var archiveName = Path.GetFileNameWithoutExtension(firstPath);

    var service = new ZipArchiveService();
    var options = new ArchiveOptions
    {
        SourcePaths = sourcePaths,
        DestinationFolder = destFolder,
        ArchiveName = archiveName,
        Mode = ArchiveMode.SingleArchive,
        OnConflict = ConflictBehavior.Skip,
    };

    string title = $"Archiving: {archiveName}";
    await RunWithProgressWindowAsync(title,
        (progress, ct) => service.ArchiveAsync(options, progress, ct))
        .ConfigureAwait(false);
}

// -------------------------------------------------------------------------
// Launches Archiver.ProgressWindow.exe with a named pipe for live progress
// and cancellation. Falls back to silent direct-service operation if
// Archiver.ProgressWindow.exe is not found alongside this executable.
//
// Named pipe protocol (newline-delimited UTF-8 JSON):
//   ProgressWindow → Shell: {"type":"cancel"}
//   Shell → ProgressWindow: {"type":"progress","percent":N,"bytesTransferred":N,"totalBytes":N}
//   Shell → ProgressWindow: {"type":"complete","success":true}
//   Shell → ProgressWindow: {"type":"complete","success":false,"errorSummary":"N error(s)"}
//   Shell → ProgressWindow: {"type":"cancelled"}
// -------------------------------------------------------------------------
static async Task<ArchiveResult> RunWithProgressWindowAsync(
    string title,
    Func<IProgress<ProgressReport>, CancellationToken, Task<ArchiveResult>> op)
{
    string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty) ?? string.Empty;
    string pwExe = Path.Combine(exeDir, "Archiver.ProgressWindow.exe");

    // Fallback: run silently if ProgressWindow is not co-deployed.
    if (!File.Exists(pwExe))
        return await op(null!, CancellationToken.None).ConfigureAwait(false);

    string pipeName = $"pakko-{Guid.NewGuid():N}";
    using var pipe = new NamedPipeServerStream(
        pipeName,
        PipeDirection.InOut,
        maxNumberOfServerInstances: 1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    var pwProc = Process.Start(new ProcessStartInfo(pwExe)
    {
        Arguments = $"--pipe {pipeName} --title \"{title}\"",
        UseShellExecute = false,
    });

    if (pwProc is null)
        return await op(null!, CancellationToken.None).ConfigureAwait(false);

    // Wait up to 5 seconds for ProgressWindow to connect.
    using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    try
    {
        await pipe.WaitForConnectionAsync(connectCts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        try { pwProc.Kill(); } catch { }
        return await op(null!, CancellationToken.None).ConfigureAwait(false);
    }

    using var reader = new StreamReader(pipe, Encoding.UTF8,
        detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
    using var writer = new StreamWriter(pipe, Encoding.UTF8,
        bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

    // Channel bridges IProgress callbacks (any thread) to the write task (sequential).
    var ch = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    using var cts = new CancellationTokenSource();

    // Write task: drains the channel and sends JSON lines to ProgressWindow.
    var writeTask = Task.Run(async () =>
    {
        await foreach (var msg in ch.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try { writer.WriteLine(msg); }
            catch (IOException) { break; }
        }
    });

    // Read task: receives cancel signal from ProgressWindow.
    var readTask = Task.Run(async () =>
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;
                if (line.Contains("\"cancel\""))
                    cts.Cancel();
            }
        }
        catch (IOException) { /* ProgressWindow disconnected */ }
    });

    // Progress callback posts to the channel (non-blocking).
    var progress = new Progress<ProgressReport>(r =>
        ch.Writer.TryWrite(
            $"{{\"type\":\"progress\",\"percent\":{r.Percent}," +
            $"\"bytesTransferred\":{r.BytesTransferred},\"totalBytes\":{r.TotalBytes}}}"));

    ArchiveResult result;
    try
    {
        result = await op(progress, cts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        ch.Writer.TryWrite("{\"type\":\"cancelled\"}");
        ch.Writer.Complete();
        await writeTask.ConfigureAwait(false);
        await pwProc.WaitForExitAsync().ConfigureAwait(false);
        return new ArchiveResult { Success = false };
    }

    string completionMsg = result.Success && result.Errors.Count == 0
        ? "{\"type\":\"complete\",\"success\":true}"
        : $"{{\"type\":\"complete\",\"success\":false,\"errorSummary\":\"{result.Errors.Count} error(s)\"}}";
    ch.Writer.TryWrite(completionMsg);
    ch.Writer.Complete();

    await writeTask.ConfigureAwait(false);
    await readTask.ConfigureAwait(false);
    await pwProc.WaitForExitAsync().ConfigureAwait(false);
    return result;
}
