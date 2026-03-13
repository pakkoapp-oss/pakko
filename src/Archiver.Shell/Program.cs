using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Shell;

var command = ShellArgumentParser.Parse(args);

if (command.Type == CommandType.Invalid)
{
    // WinExe: no console window shown. Exit silently — T-F54 will surface
    // errors through ProgressWindow once that project is implemented.
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

        // TODO T-F54: Replace direct Archiver.Core call with an
        // Archiver.ProgressWindow process launch. Pass operation parameters
        // as command-line arguments; wire a named pipe for progress updates
        // and cancellation. Wait for the ProgressWindow process to exit.
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destFolder,
            Mode = ExtractMode.SeparateFolders,
            OnConflict = ConflictBehavior.Skip,
        };

        await service.ExtractAsync(options).ConfigureAwait(false);
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

        // TODO T-F54: Replace direct Archiver.Core call with an
        // Archiver.ProgressWindow process launch. Pass operation parameters
        // as command-line arguments; wire a named pipe for progress updates
        // and cancellation. Wait for the ProgressWindow process to exit.
        var options = new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destFolder,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Skip,
        };

        await service.ExtractAsync(options).ConfigureAwait(false);
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

    // TODO T-F54: Replace direct Archiver.Core call with an
    // Archiver.ProgressWindow process launch. Pass operation parameters
    // as command-line arguments; wire a named pipe for progress updates
    // and cancellation. Wait for the ProgressWindow process to exit.
    var service = new ZipArchiveService();
    var options = new ArchiveOptions
    {
        SourcePaths = sourcePaths,
        DestinationFolder = destFolder,
        ArchiveName = archiveName,
        Mode = ArchiveMode.SingleArchive,
        OnConflict = ConflictBehavior.Skip,
    };

    await service.ArchiveAsync(options).ConfigureAwait(false);
}
