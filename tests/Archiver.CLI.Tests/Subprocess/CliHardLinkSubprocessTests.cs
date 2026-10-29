using System.ComponentModel;
using System.Runtime.InteropServices;
using FluentAssertions;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// T-F392: 'pakko a -ttar' over a folder holding two names of one file, through the real exe (the
/// AOT build in CI) and the real tar.exe. 7-Zip stores both names as files here; tar.exe would
/// store a link entry Pakko refuses, so Pakko refuses the creation and names both.
/// </summary>
public sealed partial class CliHardLinkSubprocessTests : IAsyncLifetime
{
    private readonly TestTempFolder _temp = new("pakko-cli-hardlink-");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _temp.DeleteAsync();

    private (string Folder, string First, string Second) LinkedPair()
    {
        string folder = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(folder);
        string first = Path.Combine(folder, "first.txt");
        File.WriteAllText(first, "one file, two names");
        string second = Path.Combine(folder, "second.txt");
        if (!CreateHardLinkW(second, first, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return (folder, first, second);
    }

    [RequiresTarExe]
    public void Tar_FolderWithTwoNamesOfOneFile_IsExitTwoNamesBothAndWritesNothing()
    {
        (string folder, string first, string second) = LinkedPair();
        string archive = Path.Combine(_temp.Path, "out.tar");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-ttar", archive, folder);

        exitCode.Should().Be(2, stdErr);
        stdErr.Should().Contain(first).And.Contain(second).And.Contain("ZIP or 7z");
        File.Exists(archive).Should().BeFalse();
    }

    [RequiresTarExe]
    public void Zip_FolderWithTwoNamesOfOneFile_StoresBoth()
    {
        (string folder, _, _) = LinkedPair();
        string archive = Path.Combine(_temp.Path, "out.zip");

        (int exitCode, _, string stdErr) = CliProcessRunner.Run("a", "-tzip", archive, folder);

        exitCode.Should().Be(0, stdErr);
        File.Exists(archive).Should().BeTrue();
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string fileName, string existingFileName, nint securityAttributes);
}
