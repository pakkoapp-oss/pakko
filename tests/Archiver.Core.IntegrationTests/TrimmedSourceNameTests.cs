using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.Versioning;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F509: a file or folder named "a.txt " or "d." (WSL, a share, a \\?\ path) is, by its plain
/// path, another name to Windows, which drops the trailing space or dot. With a twin beside it
/// ("a.txt", "d") the plain path read the twin: its bytes went into the archive under the
/// source's name, and the run counted the source as done, so "move the sources to the Recycle Bin"
/// would recycle the twin. Such a name is now refused: never another file's bytes, never counted
/// done. tar.exe's own walk of a folder reads these names correctly and still does.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("TarSandbox")]
public sealed class TrimmedSourceNameTests : IDisposable
{
    private const string Literal = @"\\?\";
    private const string TwinBytes = "TWIN, not a source";
    private readonly TempDirectory _temp = new();
    private readonly PakkoServices _services = PakkoServices.Create(new GroupPolicyOptions());

    public void Dispose()
    {
        foreach (string file in Directory.GetFiles(Literal + _temp.Path, "*", SearchOption.AllDirectories))
            File.Delete(file);
        _temp.Dispose();
        if (Directory.Exists(Literal + _temp.Path))
            Directory.Delete(Literal + _temp.Path, recursive: true);
    }

    public static TheoryData<ArchiveContainerFormat, bool, bool> Matrix()
    {
        var data = new TheoryData<ArchiveContainerFormat, bool, bool>();
        foreach (ArchiveContainerFormat format in new[] { ArchiveContainerFormat.Zip, ArchiveContainerFormat.TarGz })
            foreach (bool twin in new[] { true, false })
                foreach (bool literal in new[] { false, true })
                    data.Add(format, twin, literal);
        return data;
    }

    [Theory, MemberData(nameof(Matrix))]
    public async Task Create_FileNamedWithATrailingSpace_IsRefusedAndTheOthersArchived(ArchiveContainerFormat format, bool twin, bool literal)
    {
        string folder = Folder("f");
        string trimmed = WriteLiteral(Path.Combine(folder, "a.txt "), "SPACED");
        if (twin)
            File.WriteAllText(Path.Combine(folder, "a.txt"), TwinBytes);
        string ok = WriteLiteral(Path.Combine(folder, "ok.txt"), "OK");

        ArchiveResult result = await CreateAsync(format, literal ? Literal + trimmed : trimmed, ok);

        RefusedAlone(result, trimmed);
        Contents(result, format).Should().Equal(new Dictionary<string, string> { ["ok.txt"] = "OK" });
    }

    [Theory, MemberData(nameof(Matrix))]
    public async Task Create_FolderNamedWithATrailingDot_IsRefusedAndTheOthersArchived(ArchiveContainerFormat format, bool twin, bool literal)
    {
        string folder = Folder("d");
        string trimmed = Path.Combine(folder, "d.");
        WriteLiteral(Path.Combine(trimmed, "x", "y.txt"), "IN DOTTED");
        if (twin)
            WriteLiteral(Path.Combine(folder, "d", "x", "y.txt"), TwinBytes);
        string ok = WriteLiteral(Path.Combine(folder, "ok.txt"), "OK");

        ArchiveResult result = await CreateAsync(format, literal ? Literal + trimmed : trimmed, ok);

        RefusedAlone(result, trimmed);
        Contents(result, format).Should().Equal(new Dictionary<string, string> { ["ok.txt"] = "OK" });
    }

    // ZIP walks the folder itself: the file is refused inside it and the folder is not done.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_ZipFolderHoldingTwins_RefusesTheTrimmedFileAndKeepsTheFolder(bool nested)
    {
        string src = Folder(Path.Combine("z", "src"));
        string trimmed = nested
            ? WriteLiteral(Path.Combine(src, "d.", "x", "y.txt"), "IN DOTTED")
            : WriteLiteral(Path.Combine(src, "a.txt "), "SPACED");
        string twinName = nested ? "d/x/y.txt" : "a.txt";
        WriteLiteral(Path.Combine(src, twinName.Replace('/', '\\')), TwinBytes);

        ArchiveResult result = await CreateAsync(ArchiveContainerFormat.Zip, src);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNameTrimmed);
        result.Errors[0].SourcePath.Should().Be(trimmed);
        result.FullyProcessedSources.Should().BeEmpty("a folder with a file left out is never deleted");
        Contents(result, ArchiveContainerFormat.Zip).Should().Equal(new Dictionary<string, string> { ["src/" + twinName] = TwinBytes });
    }

    // tar.exe walks the folder itself and reads both names; through Core's own walk too, which a
    // filter or a second folder of the same name brings in.
    [Theory]
    [InlineData("plain")]
    [InlineData("filter")]
    [InlineData("collision")]
    public async Task Create_TarFolderHoldingTwins_ArchivesBothWithTheirOwnBytes(string shape)
    {
        string src = Folder(Path.Combine("t1", "src"));
        WriteLiteral(Path.Combine(src, "a.txt "), "SPACED 1");
        WriteLiteral(Path.Combine(src, "a.txt"), "PLAIN 1");
        WriteLiteral(Path.Combine(src, "skip.log"), "LOG");
        var expected = new Dictionary<string, string> { ["src/a.txt "] = "SPACED 1", ["src/a.txt"] = "PLAIN 1", ["src/skip.log"] = "LOG" };
        string[] sources = [src];
        if (shape == "collision")
        {
            string second = Folder(Path.Combine("t2", "src"));
            WriteLiteral(Path.Combine(second, "a.txt "), "SPACED 2");
            WriteLiteral(Path.Combine(second, "a.txt"), "PLAIN 2");
            sources = [src, second];
            expected["src (1)/a.txt "] = "SPACED 2";
            expected["src (1)/a.txt"] = "PLAIN 2";
        }
        if (shape == "filter")
            expected.Remove("src/skip.log");

        ArchiveResult result = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources, DestinationFolder = Path.Combine(_temp.Path, "out-" + shape), ArchiveName = "x", Format = ArchiveContainerFormat.Tar,
            EntryFilter = shape == "filter" ? (name, _) => !name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) : null,
        });

        result.Errors.Should().BeEmpty();
        Contents(result, ArchiveContainerFormat.Tar).Should().BeEquivalentTo(expected);
    }

    // The archive read is the one named: "x.zip " beside "x.zip" opened the twin, and the twin was
    // then counted done - extract, then "move the archives to the Recycle Bin".
    [Theory]
    [InlineData(".zip", false)]
    [InlineData(".zip", true)]
    [InlineData(".tar", false)]
    [InlineData(".tar", true)]
    public async Task Read_ArchiveNamedWithATrailingSpace_IsRefused(string extension, bool literal)
    {
        string folder = Folder("r" + extension);
        string content = Folder(Path.Combine("r" + extension, "content"));
        File.WriteAllText(Path.Combine(content, "inner.txt"), "inner");
        string twin = Path.Combine(folder, "x" + extension);
        if (extension == ".zip")
            ZipFile.CreateFromDirectory(content, twin);
        else
            TarFile.CreateFromDirectory(content, twin, includeBaseDirectory: false);
        string trimmed = Path.Combine(folder, "x" + extension + " ");
        File.Copy(twin, Literal + trimmed);
        string archive = literal ? Literal + trimmed : trimmed;
        IExtractionRouter extraction = await _services.CreateExtractionRouterAsync();
        IArchiveListingRouter listing = await _services.CreateListingRouterAsync();
        string destination = Path.Combine(_temp.Path, "x-out" + extension + literal);

        ArchiveResult extracted = await extraction.ExtractAsync(new ExtractOptions { ArchivePaths = [archive], DestinationFolder = destination });
        ArchiveResult tested = await extraction.TestAsync([archive]);
        ArchiveListResult listed = await listing.ListEntriesAsync(archive);

        foreach (ArchiveResult result in new[] { extracted, tested })
        {
            result.Errors.Select(e => e.Text!.Code).Concat(result.SkippedFiles.Select(s => s.Text!.Code))
                .Should().Equal([MessageCode.SourceNameTrimmed], $"skipped: {string.Join(" | ", result.SkippedFiles.Select(s => s.Reason))}");
            result.FullyProcessedSources.Should().BeEmpty();
        }
        listed.Success.Should().BeFalse();
        listed.ErrorText!.Code.Should().Be(MessageCode.SourceNameTrimmed);
        Directory.Exists(destination).Should().BeFalse("nothing is extracted");
    }

    // Repair writes: "x.zip " opened its twin "x.zip", whose set is beside it. Refused, the twin untouched.
    [Fact]
    public async Task Recovery_ArchiveNamedWithATrailingSpace_IsRefusedAndTheTwinUntouched()
    {
        string folder = Folder("rec");
        WriteLiteral(Path.Combine(folder, "content", "inner.txt"), "inner");
        ArchiveResult created = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Path.Combine(folder, "content")], DestinationFolder = folder, ArchiveName = "x", RecoveryPercent = 5,
        });
        created.Errors.Should().BeEmpty();
        string twin = Path.Combine(folder, "x.zip");
        string trimmed = twin + " ";
        File.Copy(twin, Literal + trimmed);
        byte[] twinBefore = File.ReadAllBytes(twin);
        IExtractionRouter extraction = await _services.CreateExtractionRouterAsync();

        ArchiveResult repaired = await _services.RecoveryService.RepairAsync(new RepairOptions { Paths = [trimmed] });
        ArchiveResult tested = await extraction.TestAsync([trimmed], verifyRecoveryData: true);

        foreach (ArchiveResult result in new[] { repaired, tested })
        {
            result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNameTrimmed);
            result.CreatedFiles.Should().BeEmpty();
        }
        File.ReadAllBytes(twin).Should().Equal(twinBefore);
    }

    private string Folder(string relative)
    {
        string folder = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string WriteLiteral(string path, string content)
    {
        Directory.CreateDirectory(Literal + Path.GetDirectoryName(path));
        File.WriteAllText(Literal + path, content);
        return path;
    }

    private Task<ArchiveResult> CreateAsync(ArchiveContainerFormat format, params string[] sources) =>
        _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources, DestinationFolder = Path.Combine(_temp.Path, "out-" + Guid.NewGuid().ToString("N")[..6]), ArchiveName = "x", Format = format,
        });

    private static void RefusedAlone(ArchiveResult result, string trimmed)
    {
        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNameTrimmed);
        result.Errors[0].SourcePath.Should().EndWith(trimmed);
        result.FullyProcessedSources.Should().NotContain(source => source.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase));
        result.Sources.Should().Contain(source => source.Path.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase) && source.Outcome == SourceOutcome.NotProcessed);
    }

    // Entry name -> content; folder entries are left out.
    private static Dictionary<string, string> Contents(ArchiveResult result, ArchiveContainerFormat format)
    {
        result.CreatedFiles.Should().ContainSingle();
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        if (format == ArchiveContainerFormat.Zip)
        {
            using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles[0]);
            foreach (ZipArchiveEntry entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
            {
                using var reader = new StreamReader(entry.Open());
                contents.Add(entry.FullName, reader.ReadToEnd());
            }
            return contents;
        }
        using Stream file = File.OpenRead(result.CreatedFiles[0]);
        using Stream data = format == ArchiveContainerFormat.TarGz ? new GZipStream(file, CompressionMode.Decompress) : file;
        using var tar = new TarReader(data);
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.DataStream is null)
                continue;
            using var reader = new StreamReader(entry.DataStream);
            contents.Add(entry.Name, reader.ReadToEnd());
        }
        return contents;
    }
}
