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
    // Each ZIP writer walks the folder: the sequential one (Optimal), the parallel one (Fastest, or
    // over 64 files) and the one-archive-per-item one (T-F503).
    [Theory]
    [InlineData(false, "sequential")]
    [InlineData(true, "sequential")]
    [InlineData(false, "fastest")]
    [InlineData(true, "fastest")]
    [InlineData(false, "many")]
    [InlineData(false, "separate")]
    [InlineData(true, "separate")]
    public async Task Create_ZipFolderHoldingTwins_RefusesTheTrimmedFileAndKeepsTheFolder(bool nested, string writer)
    {
        string src = Folder(Path.Combine("z", "src"));
        string trimmedFolder = Path.Combine(src, "d.");
        string trimmed = nested
            ? WriteLiteral(Path.Combine(trimmedFolder, "x", "y.txt"), "IN DOTTED")
            : WriteLiteral(Path.Combine(src, "a.txt "), "SPACED");
        string twinName = nested ? "d/x/y.txt" : "a.txt";
        WriteLiteral(Path.Combine(src, twinName.Replace('/', '\\')), TwinBytes);
        var expected = new Dictionary<string, string> { ["src/" + twinName] = TwinBytes };
        if (writer == "many")
        {
            for (int i = 0; i < 70; i++)
            {
                File.WriteAllText(Path.Combine(src, $"f{i:D2}.txt"), "F");
                expected[$"src/f{i:D2}.txt"] = "F";
            }
        }

        ArchiveResult result = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src], DestinationFolder = Path.Combine(_temp.Path, "out-" + writer + nested), ArchiveName = "x",
            CompressionLevel = writer == "fastest" ? System.IO.Compression.CompressionLevel.Fastest : System.IO.Compression.CompressionLevel.Optimal,
            Mode = writer == "separate" ? ArchiveMode.SeparateArchives : ArchiveMode.SingleArchive,
        });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNameTrimmed);
        result.Errors[0].SourcePath.Should().Be(nested ? trimmedFolder : trimmed);
        result.FullyProcessedSources.Should().BeEmpty("a folder with a file left out is never deleted");
        Contents(result, ArchiveContainerFormat.Zip).Should().Equal(expected);
    }

    // tar.exe walks the folder itself and would read both names, but Core's checks of what it will
    // meet (the filter, links, the code page, hard-link pairs) open them by their plain paths: the
    // source holding such a name is refused whole, the others archived.
    [Theory]
    [InlineData("file")]
    [InlineData("folder")]
    [InlineData("filter")]
    [InlineData("collision")]
    public async Task Create_TarFolderHoldingATrimmedName_IsRefusedWhole(string shape)
    {
        string src = Folder(Path.Combine("t1", "src"));
        string trimmed = shape == "folder" ? Path.Combine(src, "d.") : Path.Combine(src, "a.txt ");
        if (shape == "folder")
            WriteLiteral(Path.Combine(trimmed, "in.txt"), "IN DOTTED");
        else
            WriteLiteral(trimmed, "SPACED 1");
        WriteLiteral(Path.Combine(src, "a.txt"), "PLAIN 1");
        WriteLiteral(Path.Combine(src, "skip.log"), "LOG");
        string other = WriteLiteral(Path.Combine(_temp.Path, "t3", "other.txt"), "OTHER");
        string[] sources = [src, other];
        if (shape == "collision")
        {
            string second = Folder(Path.Combine("t2", "src"));
            WriteLiteral(Path.Combine(second, "a.txt"), "PLAIN 2");
            sources = [src, second, other];
        }

        ArchiveResult result = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources, DestinationFolder = Path.Combine(_temp.Path, "out-" + shape), ArchiveName = "x", Format = ArchiveContainerFormat.Tar,
            EntryFilter = shape == "filter" ? (name, _) => !name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) : null,
        });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNameTrimmed);
        result.Errors[0].SourcePath.Should().Be(src);
        result.FullyProcessedSources.Should().NotContain(src);
        Dictionary<string, string> contents = Contents(result, ArchiveContainerFormat.Tar);
        contents.Values.Should().NotContain(["SPACED 1", "PLAIN 1", "IN DOTTED", "LOG"], "nothing of the refused source is archived");
        contents["other.txt"].Should().Be("OTHER");
        if (shape == "collision")
            contents["src/a.txt"].Should().Be("PLAIN 2", "the second folder no longer collides with the refused one");
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

    // Hashing reads the bytes: "a.txt " hashed its twin, given alone or met inside a folder.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hash_NameWindowsTrims_IsAnErrorNeverTheTwinsHash(bool inFolder)
    {
        string src = Folder(Path.Combine("h", "src"));
        string trimmed = WriteLiteral(Path.Combine(src, "a.txt "), "SPACED");
        WriteLiteral(Path.Combine(src, "a.txt"), TwinBytes);

        HashResult result = await FileHashService.ComputeAsync([inFolder ? src : trimmed], HashAlgorithmKind.Crc32, null, CancellationToken.None);

        HashEntry entry = result.Entries.Should().ContainSingle(e => e.SourcePath == trimmed).Subject;
        entry.Hash.Should().BeNull();
        entry.ErrorText!.Code.Should().Be(MessageCode.SourceNameTrimmed);
    }

    // T-F475 refused "CON.zip" in a folder; "C:\w\d." is "C:\w\d" to Windows, so no folder check
    // judged the path the archive would really get. Such a destination is refused.
    [Theory]
    [InlineData(ArchiveContainerFormat.Zip)]
    [InlineData(ArchiveContainerFormat.TarGz)]
    public async Task Create_DestinationWindowsTrims_IsRefused(ArchiveContainerFormat format)
    {
        string source = WriteLiteral(Path.Combine(_temp.Path, "dst", "a.txt"), "A");
        string destination = Path.Combine(_temp.Path, "dst", "out.");

        ArchiveResult result = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [source], DestinationFolder = destination, ArchiveName = "x", Format = format,
        });

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("ends in a space or a dot");
        result.CreatedFiles.Should().BeEmpty();
        Directory.Exists(Path.Combine(_temp.Path, "dst", "out")).Should().BeFalse("nothing is written through the trimmed name");
    }

    // A refused source still counts for what the run was asked: two sources name the archive after
    // their folder, and a single-file format still refuses two.
    [Fact]
    public async Task Create_RefusedSource_StillCountsForNamingAndForASingleFileFormat()
    {
        string folder = Folder("named");
        WriteLiteral(Path.Combine(folder, "a.txt "), "SPACED");
        string kept = WriteLiteral(Path.Combine(folder, "b.txt"), "B");
        string[] sources = [Path.Combine(folder, "a.txt "), kept];

        ArchiveResult zip = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources, DestinationFolder = Path.Combine(_temp.Path, "out-named"),
        });
        ArchiveResult gz = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources, DestinationFolder = Path.Combine(_temp.Path, "out-gz"), Format = ArchiveContainerFormat.GZip,
        });

        Path.GetFileName(zip.CreatedFiles.Should().ContainSingle().Subject).Should().Be("named.zip");
        gz.CreatedFiles.Should().BeEmpty();
        gz.Errors.Select(e => e.Text!.Code).Should().Contain(MessageCode.SingleFileFormatNeedsOneFile);
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
