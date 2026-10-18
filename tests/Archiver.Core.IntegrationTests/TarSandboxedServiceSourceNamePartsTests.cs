using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F414: <see cref="ArchiveOptions.SourceNameParts"/> through the real tar.exe. A name kept with
/// its folders ("dir/sub") goes to tar.exe as that name under the folder above them; a clash of
/// two such names is staged under the folder too, so only the last part is numbered.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceSourceNamePartsTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string MakeTree()
    {
        string sub = Path.Combine(_temp.Path, "src", "dir", "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "s.txt"), "s");
        File.WriteAllText(Path.Combine(_temp.Path, "src", "dir", "c.txt"), "c");
        return Path.Combine(_temp.Path, "src");
    }

    private async Task<(ArchiveResult Result, string[] Files)> CompressAsync(
        IReadOnlyList<string> sources, IReadOnlyList<int>? parts, ArchiveContainerFormat format = ArchiveContainerFormat.Tar)
    {
        string dest = Path.Combine(_temp.Path, "out-" + Path.GetRandomFileName());
        ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            SourceNameParts = parts,
            DestinationFolder = dest,
            ArchiveName = "o",
            Format = format,
        });
        string[] files = [];
        if (result.CreatedFiles.Count == 1)
        {
            ArchiveListResult listed = await _sut.ListEntriesAsync(result.CreatedFiles[0]);
            listed.Success.Should().BeTrue(because: listed.ErrorMessage);
            files = [.. listed.Entries.Where(e => !e.IsDirectory).Select(e => e.Path.Replace('\\', '/')).Order(StringComparer.Ordinal)];
        }
        return (result, files);
    }

    [Integration]
    public async Task CompressAsync_FolderWithTwoParts_KeepsParentInEntryNames()
    {
        string src = MakeTree();

        (ArchiveResult result, string[] files) = await CompressAsync([Path.Combine(src, "dir", "sub")], [2]);

        result.Errors.Should().BeEmpty();
        files.Should().Equal("dir/sub/s.txt");
    }

    [Integration]
    public async Task CompressAsync_FileWithTwoParts_AndAFolderWithOne()
    {
        string src = MakeTree();
        string other = Path.Combine(_temp.Path, "other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "o.txt"), "o");

        (ArchiveResult result, string[] files) = await CompressAsync([Path.Combine(src, "dir", "c.txt"), other], [2, 1]);

        result.Errors.Should().BeEmpty();
        files.Should().Equal("dir/c.txt", "other/o.txt");
    }

    [Integration]
    public async Task CompressAsync_SameNameTwice_NumbersTheLastPartOnly()
    {
        string src = MakeTree();
        string file = Path.Combine(src, "dir", "c.txt");

        (ArchiveResult result, string[] files) = await CompressAsync([file, file], [2, 2]);

        result.Errors.Should().BeEmpty();
        files.Should().Equal("dir/c (1).txt", "dir/c.txt");
    }

    // The closing review's case, as in ZipArchiveServiceSourceNamePartsTests.
    [Integration]
    public async Task CompressAsync_RenameNeverLandsOnAFolderOfAnotherName()
    {
        foreach (string folder in new[] { "A", "B" })
        {
            Directory.CreateDirectory(Path.Combine(_temp.Path, folder, "sub"));
            File.WriteAllText(Path.Combine(_temp.Path, folder, "sub", "z"), folder);
        }
        string c = Path.Combine(_temp.Path, "C", "sub (1)");
        Directory.CreateDirectory(c);
        File.WriteAllText(Path.Combine(c, "z"), "C");

        (ArchiveResult result, string[] files) = await CompressAsync(
            [Path.Combine(_temp.Path, "A", "sub"), Path.Combine(_temp.Path, "B", "sub"), Path.Combine(c, "z")], [1, 1, 2]);

        result.Errors.Should().BeEmpty();
        files.Should().OnlyHaveUniqueItems().And.HaveCount(3).And.Contain("sub (1)/z");
    }

    [Integration]
    public async Task CompressAsync_OneNameInsideAnother_RefusedBeforeTarRuns()
    {
        string src = MakeTree();

        (ArchiveResult result, _) = await CompressAsync([Path.Combine(src, "dir"), Path.Combine(src, "dir", "c.txt")], [1, 2]);

        result.CreatedFiles.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNamesOverlap);
    }

    [SevenZipWriterFact]
    public async Task CompressAsync_SevenZip_KeepsParentInEntryNames()
    {
        string src = MakeTree();

        (ArchiveResult result, string[] files) = await CompressAsync([Path.Combine(src, "dir", "sub")], [2], ArchiveContainerFormat.SevenZip);

        result.Errors.Should().BeEmpty();
        files.Should().Equal("dir/sub/s.txt");
    }
}
