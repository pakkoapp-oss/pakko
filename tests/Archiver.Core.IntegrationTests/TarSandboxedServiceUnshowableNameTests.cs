using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F416: tar.exe 3.5.2 (Windows 10, ACP 1252) lists a UTF-8 name it cannot show in the ANSI code
/// page as "????.txt", exit 0, while extraction writes the real name. A name with '?' from the
/// listing therefore matches nothing reliably: a filter or a selection that depends on it is refused
/// with TarNameNotRepresentable; a whole-archive extraction or test goes on. A real '?' in a name
/// (it cannot be a Windows file name either) stands in for the lossy one on every machine.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceUnshowableNameTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static TarBuilder.Entry File(string name) => new() { Name = name, Content = Encoding.ASCII.GetBytes(name) };

    [Integration]
    public async Task ExtractAsync_FilterOverAnUnshowableName_IsRefusedAndWritesNothing()
    {
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Tar()], DestinationFolder = dest, Mode = ExtractMode.SingleFolder, EntryFilter = (_, _) => true,
        });

        // Extraction wraps it, as T-F266's selection that cannot be passed to tar.exe.
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("'a?b.txt'").And.Contain("tar.exe");
        Directory.EnumerateFileSystemEntries(dest).Should().BeEmpty();
    }

    [Integration]
    public async Task ExtractAsync_SelectingTheUnshowableName_IsRefused()
    {
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Tar()], DestinationFolder = NewDestination(), Mode = ExtractMode.SingleFolder, SelectedEntryPaths = ["a?b.txt"],
        });

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("'a?b.txt'").And.Contain("tar.exe");
    }

    [Integration]
    public async Task ExtractAsync_SelectingAnotherName_Works()
    {
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Tar()], DestinationFolder = dest, Mode = ExtractMode.SingleFolder, SelectedEntryPaths = ["other.txt"],
        });

        result.Errors.Should().BeEmpty();
        Directory.EnumerateFiles(dest).Select(Path.GetFileName).Should().Equal("other.txt");
    }

    [Integration]
    public async Task ExtractAsync_WholeArchive_StillWorks()
    {
        string dest = NewDestination();

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [Tar()], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
        });

        result.Errors.Should().BeEmpty();
        Directory.EnumerateFiles(dest).Should().HaveCount(2);
    }

    [Integration]
    public async Task TestAsync_FilterOverAnUnshowableName_IsRefused()
    {
        ArchiveResult result = await _sut.TestAsync([Tar()], entryFilter: (_, _) => true);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarNameNotRepresentable);
    }

    [Integration]
    public async Task TestAsync_WholeArchive_StillWorks()
    {
        ArchiveResult result = await _sut.TestAsync([Tar()]);

        result.Errors.Should().BeEmpty();
    }

    private string Tar()
    {
        string path = Path.Combine(_temp.Path, "q" + Guid.NewGuid().ToString("N")[..6] + ".tar");
        TarBuilder.WriteTar(path, [File("a?b.txt"), File("other.txt")]);
        return path;
    }

    private string NewDestination()
    {
        string dest = Path.Combine(_temp.Path, "out" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dest);
        return dest;
    }
}
