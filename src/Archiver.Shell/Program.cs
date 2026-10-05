using System.Globalization;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Messages;
using Archiver.Shell;

// T-F51: loaded once per invocation and threaded into every service Archiver.Shell constructs —
// it has no DI container, so this is the App.xaml.cs AddSingleton(GroupPolicyService.Load())
// equivalent for this frontend.
GroupPolicyOptions policy = GroupPolicyService.Load();

// T-F254: every Shell text resource is per region (de-DE); .NET's own fallback walks only the
// parent chain (de-AT -> de -> English), so map the user's language onto the one Pakko ships.
// T-F330: the language comes from the user's language list, as the App's own strings do.
CultureInfo.CurrentUICulture = ShellUiLanguage.Pick(ShellUiLanguage.ReadUserLanguages(), CultureInfo.CurrentUICulture);

ParsedCommand command = ShellArgumentParser.Parse(args);

if (command.Type == CommandType.Invalid)
{
    // WinExe: no console window shown. Exit silently.
    Environment.Exit(1);
    return;
}

// T-F235: Explorer's selection comes on stdin. A list that is missing or cut short is rejected
// whole, and said so - never a silent exit.
if (command.FilesFromStdin)
{
    StdinPathListResult list = StdinPathList.Read(Console.OpenStandardInput());
    if (list.Error is not null)
    {
        new Win32OperationUi().ShowMessage(OperationMessages.ForSelectionNotReceived());
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
