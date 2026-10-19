using Archiver.Core.Interfaces;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F419 red team, tar.exe engine: every source FullyProcessedSources names must be readable back
// out of an archive the run left on disk — otherwise the App's "Delete after" removes it for good.
[Collection("TarSandbox")]
public sealed class TarFullyProcessedAdversarialTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();
    private readonly List<string> _junctions = [];

    public void Dispose()
    {
        foreach (string junction in _junctions)
        {
            try { Directory.Delete(junction, recursive: false); } catch (IOException) { /* best-effort: TempDirectory retries */ }
        }
        _temp.Dispose();
    }

    private string Scratch(string relative)
    {
        string path = Path.Combine(_temp.Path, relative);
        path.Should().StartWith(_temp.Path, "every destructive case stays in its scratch folder");
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task<List<string>> ContentsOfAsync(IEnumerable<string> archives)
    {
        var contents = new List<string>();
        foreach (string archive in archives)
        {
            string dest = Path.Combine(_temp.Path, "check-" + Path.GetRandomFileName());
            ArchiveResult extracted = await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [archive], DestinationFolder = dest });
            extracted.Errors.Should().BeEmpty();
            contents.AddRange(Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        }
        return contents;
    }

    [TarIntegrationTheory]
    [InlineData(ArchiveContainerFormat.Tar)]
    [InlineData(ArchiveContainerFormat.TarGz)]
    public async Task Separate_TwoFoldersSameName_Overwrite_EveryDeletableSourceIsInAnArchive(ArchiveContainerFormat format)
    {
        string first = Scratch(@"x\data");
        string second = Scratch(@"y\data");
        File.WriteAllText(Path.Combine(first, "f.txt"), "FROM-X");
        File.WriteAllText(Path.Combine(second, "f.txt"), "FROM-Y");

        ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [first, second],
            DestinationFolder = Scratch("dest"),
            Mode = ArchiveMode.SeparateArchives,
            OnConflict = ConflictBehavior.Overwrite,
            Format = format,
        });

        List<string> archived = await ContentsOfAsync(result.CreatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists));
        if (result.FullyProcessedSources.Contains(first))
            archived.Should().Contain("FROM-X", $"{first} is reported deletable; CreatedFiles = [{string.Join(", ", result.CreatedFiles)}]");
        if (result.FullyProcessedSources.Contains(second))
            archived.Should().Contain("FROM-Y", $"{second} is reported deletable; CreatedFiles = [{string.Join(", ", result.CreatedFiles)}]");
    }

    [Integration]
    public async Task Separate_TwoFilesSameBaseName_Overwrite_EveryDeletableSourceIsInAnArchive()
    {
        string folder = Scratch("src");
        string txt = Path.Combine(folder, "a.txt");
        string csv = Path.Combine(folder, "a.csv");
        File.WriteAllText(txt, "TXT");
        File.WriteAllText(csv, "CSV");

        ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [txt, csv],
            DestinationFolder = Scratch("dest"),
            Mode = ArchiveMode.SeparateArchives,
            OnConflict = ConflictBehavior.Overwrite,
            Format = ArchiveContainerFormat.Tar,
        });

        List<string> archived = await ContentsOfAsync(result.CreatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists));
        if (result.FullyProcessedSources.Contains(txt))
            archived.Should().Contain("TXT", "a.txt is reported deletable");
        if (result.FullyProcessedSources.Contains(csv))
            archived.Should().Contain("CSV", "a.csv is reported deletable");
    }

    [SkipIfFormatUnsupported("7z")]
    public async Task Separate_SevenZip_TwoFoldersSameName_Overwrite_EveryDeletableSourceIsInAnArchive()
    {
        string first = Scratch(@"x\data");
        string second = Scratch(@"y\data");
        File.WriteAllText(Path.Combine(first, "f.txt"), "FROM-X");
        File.WriteAllText(Path.Combine(second, "f.txt"), "FROM-Y");

        ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [first, second],
            DestinationFolder = Scratch("dest"),
            Mode = ArchiveMode.SeparateArchives,
            OnConflict = ConflictBehavior.Overwrite,
            Format = ArchiveContainerFormat.SevenZip,
        });

        List<string> archived = await ContentsOfAsync(result.CreatedFiles.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists));
        if (result.FullyProcessedSources.Contains(first))
            archived.Should().Contain("FROM-X", $"{first} is reported deletable");
        if (result.FullyProcessedSources.Contains(second))
            archived.Should().Contain("FROM-Y", $"{second} is reported deletable");
    }

    [TarIntegrationTheory]
    [InlineData(ArchiveMode.SingleArchive)]
    [InlineData(ArchiveMode.SeparateArchives)]
    public async Task DestinationIsJunctionIntoSource_SourceNotDeletable(ArchiveMode mode)
    {
        string src = Scratch("src");
        File.WriteAllText(Path.Combine(src, "keep.txt"), "precious");
        string link = Path.Combine(_temp.Path, "j");
        DirectoryJunction.Create(link, src);
        _junctions.Add(link);

        ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = link,
            ArchiveName = "out",
            Mode = mode,
            Format = ArchiveContainerFormat.Tar,
        });

        Directory.EnumerateFiles(src, "*.tar").Should().NotBeEmpty("precondition: the archive physically sits inside the source");
        result.FullyProcessedSources.Should().NotContain(src, "deleting the source would delete the archive written into it through a junction");
    }

    [Integration]
    public async Task FileLockedExclusively_SourceNotDeletable()
    {
        string src = Scratch("src");
        File.WriteAllText(Path.Combine(src, "ok.txt"), "ok");
        string locked = Path.Combine(src, "locked.bin");
        File.WriteAllText(locked, "cannot be read");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ArchiveResult result = await _sut.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [src],
            DestinationFolder = Scratch("dest"),
            Format = ArchiveContainerFormat.Tar,
        });

        result.FullyProcessedSources.Should().NotContain(src, "locked.bin is not in the archive");
    }

    [TarIntegrationTheory]
    [InlineData("a.txt", "a.txt", ConflictBehavior.Overwrite)]
    [InlineData("a.txt", "A.txt", ConflictBehavior.Overwrite)]
    [InlineData("a.txt", "a.txt.", ConflictBehavior.Overwrite)]
    [InlineData("a.txt", "A.txt", ConflictBehavior.Rename)]
    public async Task Extract_TwoEntriesSameDiskPath_ArchiveDeletableOnlyIfBothContentsOnDisk(string first, string second, ConflictBehavior onConflict)
    {
        string tar = Path.Combine(Scratch("archives"), "collide.tar");
        TarBuilder.WriteTar(tar, [
            new TarBuilder.Entry { Name = first, Content = Encoding.ASCII.GetBytes("FIRST") },
            new TarBuilder.Entry { Name = second, Content = Encoding.ASCII.GetBytes("SECOND") },
        ]);
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions { ArchivePaths = [tar], DestinationFolder = dest, OnConflict = onConflict });

        List<string> contents = Directory.Exists(dest)
            ? [.. Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).Select(File.ReadAllText)]
            : [];
        if (!(contents.Contains("FIRST") && contents.Contains("SECOND")))
            result.FullyProcessedSources.Should().NotContain(tar, $"only [{string.Join(", ", contents)}] reached the disk");
    }

    // T-F420: two archives into one folder (x *.tar, Explorer's "Extract here"): the second's
    // a.txt replaced the first's, so the first is not wholly on disk any more.
    [Integration]
    public async Task Extract_TwoArchivesIntoOneFolder_Overwrite_TheEarlierIsNotDeletable()
    {
        string folder = Scratch("archives");
        string first = Path.Combine(folder, "a1.tar"), second = Path.Combine(folder, "a2.tar");
        TarBuilder.WriteTar(first, [new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("ONE") }]);
        TarBuilder.WriteTar(second, [new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("TWO") }]);

        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [first, second], DestinationFolder = Path.Combine(_temp.Path, "out"),
            Mode = ExtractMode.SingleFolder, OnConflict = ConflictBehavior.Overwrite,
        });

        result.FullyProcessedSources.Should().Equal(second);
    }

    // T-F420 closing review: the router hands ZIP and tar archives to two engines; a tar.gz whose
    // a.txt replaced a ZIP's in the same folder leaves the ZIP not deletable either.
    [Integration]
    public async Task Router_ZipThenTarIntoOneFolder_Overwrite_TheZipIsNotDeletable()
    {
        string folder = Scratch("mixed");
        string zip = Path.Combine(folder, "a1.zip");
        using (System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("a.txt").Open()))
            writer.Write("ONE");
        string tar = Path.Combine(folder, "a2.tar");
        TarBuilder.WriteTar(tar, [new TarBuilder.Entry { Name = "a.txt", Content = Encoding.ASCII.GetBytes("TWO") }]);
        IExtractionRouter router = await PakkoServices.Create(new GroupPolicyOptions()).CreateExtractionRouterAsync();

        ArchiveResult result = await router.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip, tar], DestinationFolder = Path.Combine(_temp.Path, "mixed-out"),
            Mode = ExtractMode.SingleFolder, OnConflict = ConflictBehavior.Overwrite,
        });

        result.FullyProcessedSources.Should().Equal(tar);
    }
}

/// <summary>A theory that needs the real tar.exe; skipped where it is missing.</summary>
public sealed class TarIntegrationTheoryAttribute : TheoryAttribute
{
    public TarIntegrationTheoryAttribute()
    {
        if (!File.Exists(@"C:\Windows\System32\tar.exe"))
            Skip = "tar.exe not present at C:\\Windows\\System32\\tar.exe";
    }
}
