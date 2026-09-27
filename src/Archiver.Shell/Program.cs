using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Shell;

// T-F51: loaded once per invocation and threaded into every service Archiver.Shell constructs —
// it has no DI container, so this is the App.xaml.cs AddSingleton(GroupPolicyService.Load())
// equivalent for this frontend.
GroupPolicyOptions policy = GroupPolicyService.Load();

ParsedCommand command = ShellArgumentParser.Parse(args);

if (command.Type == CommandType.Invalid)
{
    // WinExe: no console window shown. Exit silently.
    Environment.Exit(1);
    return;
}

// T-F235: Explorer's selection comes on stdin. A list the DLL could not finish writing is rejected
// whole; the DLL reports its own failed write, so there is nothing to show here either.
if (command.FilesFromStdin)
{
    StdinPathListResult list = StdinPathList.Read(Console.OpenStandardInput());
    if (list.Error is not null)
    {
        Environment.Exit(1);
        return;
    }
    command = command with { Files = list.Paths };
}

// T-F268: every window these commands show goes through IOperationUi. The WinUI operation window
// helper is used when it starts; Win32OperationUi (the native dialogs) is its fallback.
var ui = new HelperOperationUi(new HelperProcessLauncher(), new Win32OperationUi());
var commands = new ShellCommands(ui, ShellServices.Create(policy));

switch (command.Type)
{
    case CommandType.OpenUiExtract:
        commands.OpenUi(LaunchOperation.Extract, command.Files);
        break;

    case CommandType.OpenUiArchive:
        commands.OpenUi(LaunchOperation.Archive, command.Files);
        break;

    case CommandType.OpenUiBrowse:
        commands.OpenUi(LaunchOperation.Browse, command.Files);
        break;

    case CommandType.ExtractHere:
        await commands.ExtractHereAsync(command.Files).ConfigureAwait(false);
        break;

    case CommandType.ExtractHereFlat:
        await commands.ExtractHereFlatAsync(command.Files).ConfigureAwait(false);
        break;

    case CommandType.ExtractFolder:
        await commands.ExtractFolderAsync(command.Files).ConfigureAwait(false);
        break;

    case CommandType.Archive:
        await commands.ArchiveAsync(command.Files, command.Format).ConfigureAwait(false);
        break;

    case CommandType.Test:
        await commands.TestAsync(command.Files).ConfigureAwait(false);
        break;

    case CommandType.Scan:
        await commands.ScanAsync(command.Files).ConfigureAwait(false);
        break;

    case CommandType.Hash:
        await commands.HashAsync(command.Files, command.Algorithm).ConfigureAwait(false);
        break;
}
