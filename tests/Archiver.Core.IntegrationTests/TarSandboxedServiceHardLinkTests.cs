using System.Formats.Tar;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F392: tar.exe stores a file's second name as a hard-link entry, which Pakko refuses to
/// extract, and has no switch to store the content instead. A tar-family archive that would hold
/// two names of one file is refused before tar.exe starts; ZIP and 7z store both as files.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceHardLinkTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string NewFile(params string[] segments)
    {
        string path = Path.Combine([_temp.Path, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join('/', segments));
        return path;
    }

    private string Link(string existing, params string[] segments)
    {
        string path = Path.Combine([_temp.Path, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        HardLink.Create(path, existing);
        return path;
    }

    private async Task<ArchiveResult> CompressAsync(
        IReadOnlyList<string> sources, ArchiveMode mode = ArchiveMode.SingleArchive,
        ArchiveContainerFormat format = ArchiveContainerFormat.Tar, Func<string, bool, bool>? filter = null) =>
        await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = sources,
            DestinationFolder = Path.Combine(_temp.Path, "out"),
            ArchiveName = "out",
            Mode = mode,
            Format = format,
            EntryFilter = filter,
        });

    private static List<TarEntryType> EntryTypes(string tarPath)
    {
        var types = new List<TarEntryType>();
        using var reader = new TarReader(File.OpenRead(tarPath));
        while (reader.GetNextEntry() is { } entry)
            types.Add(entry.EntryType);
        return types;
    }

    // --- Happy path: two names of one file in one folder are refused, nothing written ---

    [Integration]
    public async Task CompressAsync_TwoHardLinksToOneFileInTheSource_IsRefusedBeforeTarRuns()
    {
        string first = NewFile("src", "first.txt");
        string second = Link(first, "src", "second.txt");

        ArchiveResult result = await CompressAsync([Path.Combine(_temp.Path, "src")]);

        result.CreatedFiles.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarHardLinkPair);
        result.Errors[0].Message.Should().Contain(first).And.Contain(second);
        Directory.EnumerateFiles(Path.Combine(_temp.Path, "out")).Should().BeEmpty();
    }

    [Integration]
    public async Task CompressAsync_TwoLinkedFilesGivenAsSources_IsRefused()
    {
        string first = NewFile("a", "first.txt");
        string second = Link(first, "b", "second.txt");

        ArchiveResult result = await CompressAsync([first, second]);

        result.CreatedFiles.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarHardLinkPair);
    }

    [Integration]
    public async Task CompressAsync_PairInDifferentFoldersOfOneArchive_IsRefused()
    {
        string first = NewFile("src", "a", "first.txt");
        Link(first, "src", "b", "second.txt");

        ArchiveResult result = await CompressAsync([Path.Combine(_temp.Path, "src")]);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarHardLinkPair);
    }

    // --- Boundary: only two names in ONE archive matter ---

    [Integration]
    public async Task CompressAsync_PairSplitAcrossSeparateArchives_WritesBoth()
    {
        string first = NewFile("a", "first.txt");
        Link(first, "b", "second.txt");

        ArchiveResult result = await CompressAsync(
            [Path.Combine(_temp.Path, "a"), Path.Combine(_temp.Path, "b")], ArchiveMode.SeparateArchives);

        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().HaveCount(2);
        result.CreatedFiles.SelectMany(EntryTypes).Should().NotContain(TarEntryType.HardLink);
    }

    [Integration]
    public async Task CompressAsync_LinkToAFileOutsideTheSelection_IsStoredAsAFile()
    {
        string outside = NewFile("outside", "original.txt");
        Link(outside, "src", "copy.txt");

        ArchiveResult result = await CompressAsync([Path.Combine(_temp.Path, "src")]);

        result.Errors.Should().BeEmpty();
        EntryTypes(result.CreatedFiles.Single()).Should().NotContain(TarEntryType.HardLink);
    }

    [Integration]
    public async Task CompressAsync_OneNameOfThePairFilteredOut_WritesTheOther()
    {
        string first = NewFile("src", "first.txt");
        Link(first, "src", "second.log");

        ArchiveResult result = await CompressAsync(
            [Path.Combine(_temp.Path, "src")], filter: (name, _) => !name.EndsWith(".log", StringComparison.Ordinal));

        result.Errors.Should().BeEmpty();
        EntryTypes(result.CreatedFiles.Single()).Should().NotContain(TarEntryType.HardLink);
    }

    [SevenZipWriterFact]
    public async Task CompressAsync_SevenZip_StoresBothNamesAsFiles()
    {
        string first = NewFile("src", "first.txt");
        Link(first, "src", "second.txt");

        ArchiveResult result = await CompressAsync([Path.Combine(_temp.Path, "src")], format: ArchiveContainerFormat.SevenZip);

        result.Errors.Should().BeEmpty();
        result.CreatedFiles.Should().ContainSingle();
    }

    // --- Error path: a compressed tar is refused the same way ---

    [Integration]
    public async Task CompressAsync_TarGz_IsRefusedTheSameWay()
    {
        string first = NewFile("src", "first.txt");
        Link(first, "src", "second.txt");

        ArchiveResult result = await CompressAsync([Path.Combine(_temp.Path, "src")], format: ArchiveContainerFormat.TarGz);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarHardLinkPair);
    }
}
