using System.Formats.Tar;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F391: a link inside a source folder is left out and reported, as the ZIP engine does. Before
/// it tar.exe followed a junction (packing files from outside the selection) and stored a
/// symbolic link with its absolute target path, and Pakko then refused to extract its own archive.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceNestedLinkTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();
    private readonly string _outside;

    public TarSandboxedServiceNestedLinkTests()
    {
        _outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "outside the selection");
    }

    public void Dispose() => _temp.Dispose();

    private string NewFile(params string[] segments)
    {
        string path = Path.Combine([_temp.Path, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join('/', segments));
        return path;
    }

    private string NewJunction(params string[] segments)
    {
        string path = Path.Combine([_temp.Path, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        DirectoryJunction.Create(path, _outside);
        return path;
    }

    private static List<(string Name, TarEntryType Type)> Entries(string tarPath)
    {
        var entries = new List<(string, TarEntryType)>();
        using var reader = new TarReader(File.OpenRead(tarPath));
        while (reader.GetNextEntry() is { } entry)
            entries.Add((entry.Name.TrimEnd('/'), entry.EntryType));
        return entries;
    }

    private async Task<(ArchiveResult Result, string TarPath)> CompressAsync(string source, string? destination = null)
    {
        destination ??= Path.Combine(_temp.Path, "out");
        ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = destination,
            ArchiveName = "out",
            Format = ArchiveContainerFormat.Tar,
        });
        return (result, Path.Combine(destination, "out.tar"));
    }

    private async Task<ArchiveResult> ExtractAsync(string tarPath) =>
        await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [tarPath],
            DestinationFolder = Path.Combine(_temp.Path, "extracted"),
            OpenDestinationFolder = false,
        });

    // --- Happy path ---

    [Integration]
    public async Task CompressAsync_JunctionInsideSource_IsLeftOutReportedAndTheArchiveExtracts()
    {
        NewFile("src", "file.txt");
        NewFile("src", "sub", "real.txt");
        string link = NewJunction("src", "sub", "link");

        (ArchiveResult result, string tarPath) = await CompressAsync(Path.Combine(_temp.Path, "src"));

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle()
            .Which.Should().Match<SkippedFile>(s => s.Path == link && s.Text!.Code == MessageCode.FolderLinkNotFollowed);
        Entries(tarPath).Select(e => e.Name).Should().BeEquivalentTo("src", "src/file.txt", "src/sub", "src/sub/real.txt");

        ArchiveResult extracted = await ExtractAsync(tarPath);
        extracted.Success.Should().BeTrue(because: string.Join("; ", extracted.Errors.Select(e => e.Message)));
        Directory.GetFiles(Path.Combine(_temp.Path, "extracted"), "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName).Should().BeEquivalentTo("file.txt", "real.txt");
    }

    [SymbolicLinkFact]
    public async Task CompressAsync_SymbolicLinksInsideSource_AreLeftOutAndNoTargetPathIsStored()
    {
        NewFile("src", "file.txt");
        string fileLink = Path.Combine(_temp.Path, "src", "to-file.txt");
        string folderLink = Path.Combine(_temp.Path, "src", "to-folder");
        File.CreateSymbolicLink(fileLink, Path.Combine(_outside, "secret.txt"));
        Directory.CreateSymbolicLink(folderLink, _outside);

        (ArchiveResult result, string tarPath) = await CompressAsync(Path.Combine(_temp.Path, "src"));

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Select(s => (s.Path, s.Text!.Code)).Should().BeEquivalentTo(
        [
            (fileLink, MessageCode.ReparsePointNotArchived),
            (folderLink, MessageCode.FolderLinkNotFollowed),
        ]);
        Entries(tarPath).Should().BeEquivalentTo([("src", TarEntryType.Directory), ("src/file.txt", TarEntryType.RegularFile)]);
        File.ReadAllBytes(tarPath).AsSpan().IndexOf("outside"u8).Should().Be(-1, because: "no target path belongs in the archive");

        (await ExtractAsync(tarPath)).Success.Should().BeTrue();
    }

    // --- Security & Boundary: only the link goes, whatever its name looks like to tar.exe ---

    [Integration]
    public async Task CompressAsync_FilesThatALinksPatternCouldAlsoMatch_AreKept()
    {
        // Each real file is one an unanchored or unescaped exclude pattern for a link would take too.
        NewJunction("src", "sub", "link");
        NewFile("src", "sub", "link.txt");
        NewFile("src", "x", "src", "sub", "link");
        NewJunction("src", "br[ack]et");
        NewFile("src", "braet");
        NewJunction("src", "cash$");
        NewFile("src", "cash");
        NewFile("src", "x", "cash$");
        NewJunction("src", "^caret");
        string? nonAscii = TarCodePage.PortableNonAsciiName();
        if (nonAscii is not null)
        {
            NewJunction("src", nonAscii, nonAscii);
            NewFile("src", nonAscii, "kept.txt");
        }

        (ArchiveResult result, string tarPath) = await CompressAsync(Path.Combine(_temp.Path, "src"));

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().HaveCount(nonAscii is null ? 4 : 5);
        List<string> names = [.. Entries(tarPath).Select(e => e.Name)];
        names.Should().Contain(["src/sub/link.txt", "src/x/src/sub/link", "src/braet", "src/cash", "src/x/cash$"]);
        names.Should().NotContain(name => name.EndsWith("secret.txt", StringComparison.Ordinal));
        names.Should().NotContain(["src/sub/link", "src/br[ack]et", "src/cash$", "src/^caret"]);
        if (nonAscii is not null)
            names.Should().Contain($"src/{nonAscii}/kept.txt").And.NotContain($"src/{nonAscii}/{nonAscii}");
    }

    [Integration]
    public async Task CompressAsync_DestinationInsideASourceWithLinks_PacksNoFileOfItsOwn()
    {
        NewFile("src", "file.txt");
        NewJunction("src", "link");
        string source = Path.Combine(_temp.Path, "src");

        (ArchiveResult result, string tarPath) = await CompressAsync(source, destination: source);

        result.Errors.Should().BeEmpty();
        Entries(tarPath).Select(e => e.Name).Should().BeEquivalentTo("src", "src/file.txt");
        Directory.GetFiles(source).Select(Path.GetFileName).Should().BeEquivalentTo("file.txt", "out.tar");
    }

    // --- Misuse: a source folder named like the name list's own keyword ---

    [Integration]
    public async Task CompressAsync_LinkInsideAFolderNamedDashC_IsLeftOut()
    {
        NewFile("-C", "file.txt");
        string link = NewJunction("-C", "link");

        (ArchiveResult result, string tarPath) = await CompressAsync(Path.Combine(_temp.Path, "-C"));

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle().Which.Path.Should().Be(link);
        Entries(tarPath).Select(e => e.Name.TrimStart('.', '/')).Should().BeEquivalentTo("-C", "-C/file.txt");
    }

    // --- Error path: the link is all there is ---

    [Integration]
    public async Task CompressAsync_FolderHoldingOnlyALink_StillGivesAnArchiveOfTheFolder()
    {
        string link = NewJunction("src", "link");

        (ArchiveResult result, string tarPath) = await CompressAsync(Path.Combine(_temp.Path, "src"));

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle().Which.Path.Should().Be(link);
        Entries(tarPath).Select(e => e.Name).Should().BeEquivalentTo("src");
    }

    // --- Characterization: T-F392 (open) - a second hard link is a link entry Pakko will not extract ---

    [Integration]
    public async Task CompressAsync_TwoHardLinksToOneFile_StoresALinkEntryPakkoRefuses_TF392()
    {
        string first = NewFile("src", "first.txt");
        HardLink.Create(Path.Combine(_temp.Path, "src", "second.txt"), first);

        (ArchiveResult result, string tarPath) = await CompressAsync(Path.Combine(_temp.Path, "src"));

        result.Success.Should().BeTrue();
        Entries(tarPath).Should().Contain(e => e.Type == TarEntryType.HardLink);
        ArchiveResult extracted = await ExtractAsync(tarPath);
        extracted.Errors.Should().Contain(e => e.Text!.Code == MessageCode.TarSpecialEntry);
    }
}
