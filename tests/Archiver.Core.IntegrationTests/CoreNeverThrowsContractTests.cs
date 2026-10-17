using System.IO.Compression;
using System.Runtime.Versioning;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F406: the hard constraint "methods never throw to callers, except OperationCanceledException"
/// checked on every public Core operation, with the real engines (tar.exe included), against inputs
/// a frontend can pass: a path that is a file where a folder is expected and the reverse, a missing
/// path, a missing drive, characters Windows refuses, an empty string, a locked file, a file that
/// only has an archive's name. `pakko x a.zip -o&lt;file&gt;` died with an unhandled IOException
/// before this; a new public operation gets a row here.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("TarSandbox")]
public sealed class CoreNeverThrowsContractTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly PakkoServices _services = PakkoServices.Create(new GroupPolicyOptions());
    private readonly List<IDisposable> _locks = [];

    public void Dispose()
    {
        foreach (IDisposable held in _locks)
            held.Dispose();
        _temp.Dispose();
    }

    public static TheoryData<string> Hostile => new()
    {
        "a file", "a folder", "missing", "a missing drive", "bad characters", "empty", "locked", "not an archive", "an empty file",
    };

    // A path of the named kind. Where an archive is wanted, "a file" is a real archive.
    private string Path(string kind, string extension = ".zip")
    {
        string root = _temp.Path;
        switch (kind)
        {
            case "a file":
            {
                string file = System.IO.Path.Combine(root, "real" + extension);
                if (!File.Exists(file))
                {
                    string content = System.IO.Path.Combine(root, "content");
                    Directory.CreateDirectory(content);
                    File.WriteAllText(System.IO.Path.Combine(content, "a.txt"), "a");
                    ZipFile.CreateFromDirectory(content, file);
                }
                return file;
            }
            case "a folder":
                string folder = System.IO.Path.Combine(root, "folder" + extension);
                Directory.CreateDirectory(folder);
                return folder;
            case "missing":
                return System.IO.Path.Combine(root, "missing" + extension);
            case "a missing drive":
                char letter = Enumerable.Range('D', 23).Select(c => (char)c)
                    .First(c => !Directory.Exists(c + ":\\"));
                return letter + ":\\nowhere\\x" + extension;
            case "bad characters":
                return System.IO.Path.Combine(root, "a|b<c" + extension);
            case "empty":
                return "";
            case "locked":
                string locked = System.IO.Path.Combine(root, "locked" + extension);
                File.WriteAllText(locked, "locked");
                _locks.Add(new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
                return locked;
            case "not an archive":
                string text = System.IO.Path.Combine(root, "text" + extension);
                File.WriteAllText(text, "this is not an archive");
                return text;
            case "an empty file":
                string empty = System.IO.Path.Combine(root, "empty" + extension);
                File.WriteAllBytes(empty, []);
                return empty;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private string Source()
    {
        string file = System.IO.Path.Combine(_temp.Path, "source.txt");
        File.WriteAllText(file, "source");
        return file;
    }

    private string Destination() => System.IO.Path.Combine(_temp.Path, "out-" + Guid.NewGuid().ToString("N")[..6]);

    private static async Task NoThrow(Func<Task> operation)
    {
        Exception? escaped = null;
        try { await operation(); }
        catch (Exception ex) { escaped = ex; }
        escaped.Should().BeNull("a Core operation reports errors in its result and never throws");
    }

    // --- The archive (or archives) an operation reads ------------------------------------

    [Theory, MemberData(nameof(Hostile))]
    public Task ZipExtract_Archive(string kind) => NoThrow(() =>
        _services.ArchiveService.ExtractAsync(new ExtractOptions { ArchivePaths = [Path(kind)], DestinationFolder = Destination() }));

    [Theory, MemberData(nameof(Hostile))]
    public Task ZipTest_Archive(string kind) => NoThrow(() => _services.ArchiveService.TestAsync([Path(kind)]));

    [Theory, MemberData(nameof(Hostile))]
    public Task ZipList_Archive(string kind) => NoThrow(() => _services.ArchiveService.ListEntriesAsync(Path(kind)));

    [Theory, MemberData(nameof(Hostile))]
    public Task TarExtract_Archive(string kind) => NoThrow(() =>
        _services.TarService.ExtractAsync(new ExtractOptions { ArchivePaths = [Path(kind, ".tar.gz")], DestinationFolder = Destination() }));

    [Theory, MemberData(nameof(Hostile))]
    public Task TarTest_Archive(string kind) => NoThrow(() => _services.TarService.TestAsync([Path(kind, ".7z")]));

    [Theory, MemberData(nameof(Hostile))]
    public Task TarList_Archive(string kind) => NoThrow(() => _services.TarService.ListEntriesAsync(Path(kind, ".tar")));

    [Theory, MemberData(nameof(Hostile))]
    public Task RouterExtract_Archive(string kind) => NoThrow(async () =>
        await (await _services.CreateExtractionRouterAsync()).ExtractAsync(
            new ExtractOptions { ArchivePaths = [Path(kind, ".rar")], DestinationFolder = Destination() }));

    [Theory, MemberData(nameof(Hostile))]
    public Task RouterTest_Archive(string kind) => NoThrow(async () =>
        await (await _services.CreateExtractionRouterAsync()).TestAsync([Path(kind)], verifyRecoveryData: true));

    [Theory, MemberData(nameof(Hostile))]
    public Task RouterList_Archive(string kind) => NoThrow(async () =>
        await (await _services.CreateListingRouterAsync()).ListEntriesAsync(Path(kind, ".7z")));

    [Theory, MemberData(nameof(Hostile))]
    public Task Scan_Archive(string kind) => NoThrow(async () =>
        await (await _services.CreateScanServiceAsync()).ScanAsync(new AntivirusScanOptions { ArchivePaths = [Path(kind)] }));

    [Theory, MemberData(nameof(Hostile))]
    public Task Repair_Par2(string kind) => NoThrow(() =>
        _services.RecoveryService.RepairAsync(new RepairOptions { Paths = [Path(kind, ".par2")] }));

    [Theory, MemberData(nameof(Hostile))]
    public Task FindArchive_Par2(string kind) => NoThrow(() =>
    {
        _services.RecoveryService.FindArchive(Path(kind, ".par2"));
        _services.RecoveryService.HasFilesFor(Path(kind));
        return Task.CompletedTask;
    });

    [Theory, MemberData(nameof(Hostile))]
    public Task Hash_Path(string kind) => NoThrow(() =>
        FileHashService.ComputeAsync([Path(kind, ".bin")], HashAlgorithmKind.Sha256, null, CancellationToken.None));

    // --- The folder an operation writes into ----------------------------------------------

    [Theory, MemberData(nameof(Hostile))]
    public Task ZipExtract_Destination(string kind) => NoThrow(() =>
        _services.ArchiveService.ExtractAsync(new ExtractOptions { ArchivePaths = [Path("a file")], DestinationFolder = Path(kind, "") }));

    [Theory, MemberData(nameof(Hostile))]
    public Task TarExtract_Destination(string kind) => NoThrow(async () =>
        await _services.TarService.ExtractAsync(
            new ExtractOptions { ArchivePaths = [await TarGz()], DestinationFolder = Path(kind, "") }));

    // The engines directly, not only through the router, which can refuse first on policy.
    [Theory, MemberData(nameof(Hostile))]
    public Task ZipEngineCreate_Destination(string kind) => NoThrow(() =>
        _services.ArchiveService.ArchiveAsync(new ArchiveOptions { SourcePaths = [Source()], DestinationFolder = Path(kind, ""), ArchiveName = "out" }));

    [Theory, MemberData(nameof(Hostile))]
    public Task ZipEngineCreate_Source(string kind) => NoThrow(() =>
        _services.ArchiveService.ArchiveAsync(new ArchiveOptions { SourcePaths = [Path(kind, ".txt")], DestinationFolder = Destination(), ArchiveName = "out" }));

    [Theory, MemberData(nameof(Hostile))]
    public Task TarEngineCompress_Destination(string kind) => NoThrow(() =>
        _services.TarService.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [Source()], DestinationFolder = Path(kind, ""), ArchiveName = "out", Format = ArchiveContainerFormat.TarGz,
        }));

    [Theory, MemberData(nameof(Hostile))]
    public Task TarEngineCompress_Source(string kind) => NoThrow(() =>
        _services.TarService.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [Path(kind, ".txt")], DestinationFolder = Destination(), ArchiveName = "out", Format = ArchiveContainerFormat.TarGz,
        }));

    [Theory, MemberData(nameof(Hostile))]
    public Task ZipCreate_Destination(string kind) => NoThrow(() =>
        _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Source()], DestinationFolder = Path(kind, ""), ArchiveName = "out", RecoveryPercent = 5,
        }));

    [Theory, MemberData(nameof(Hostile))]
    public Task ZipCreateSeparate_Destination(string kind) => NoThrow(() =>
        _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Source()], DestinationFolder = Path(kind, ""), Mode = ArchiveMode.SeparateArchives,
        }));

    [Theory, MemberData(nameof(Hostile))]
    public Task TarCreate_Destination(string kind) => NoThrow(() =>
        _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Source()], DestinationFolder = Path(kind, ""), ArchiveName = "out", Format = ArchiveContainerFormat.TarGz,
        }));

    [Theory, MemberData(nameof(Hostile))]
    public Task Repair_Output(string kind) => NoThrow(async () =>
        await _services.RecoveryService.RepairAsync(new RepairOptions { Paths = [await Par2()], OutputDirectory = Path(kind, "") }));

    // --- What an operation archives ------------------------------------------------------

    [Theory, MemberData(nameof(Hostile))]
    public Task ZipCreate_Source(string kind) => NoThrow(() =>
        _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Path(kind, ".txt")], DestinationFolder = Destination(), ArchiveName = "out", RecoveryPercent = 5,
        }));

    [Theory, MemberData(nameof(Hostile))]
    public Task TarCreate_Source(string kind) => NoThrow(() =>
        _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Path(kind, ".txt")], DestinationFolder = Destination(), ArchiveName = "out", Format = ArchiveContainerFormat.TarGz,
        }));

    [Theory, MemberData(nameof(Hostile))]
    public Task SevenZipCreate_Source(string kind) => NoThrow(() =>
        _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Path(kind, ".txt")], DestinationFolder = Destination(), ArchiveName = "out", Format = ArchiveContainerFormat.SevenZip,
        }));

    // --- Completeness: every public operation has rows above -------------------------------

    // (interface or class, method) -> the tests above that cover it. A new public method on one of
    // these types fails CompletenessTable_MatchesThePublicOperations until it gets rows here.
    private static readonly Dictionary<string, string[]> Coverage = new()
    {
        ["IArchiveService.ArchiveAsync"] = [nameof(ZipEngineCreate_Destination), nameof(ZipEngineCreate_Source)],
        ["IArchiveService.ExtractAsync"] = [nameof(ZipExtract_Archive), nameof(ZipExtract_Destination)],
        ["IArchiveService.TestAsync"] = [nameof(ZipTest_Archive)],
        ["IArchiveService.ListEntriesAsync"] = [nameof(ZipList_Archive)],
        ["ITarService.ExtractAsync"] = [nameof(TarExtract_Archive), nameof(TarExtract_Destination)],
        ["ITarService.TestAsync"] = [nameof(TarTest_Archive)],
        ["ITarService.ListEntriesAsync"] = [nameof(TarList_Archive)],
        ["ITarService.CompressAsync"] = [nameof(TarEngineCompress_Destination), nameof(TarEngineCompress_Source)],
        ["IExtractionRouter.ExtractAsync"] = [nameof(RouterExtract_Archive)],
        ["IExtractionRouter.TestAsync"] = [nameof(RouterTest_Archive)],
        ["IArchiveCreationRouter.ArchiveAsync"] =
            [nameof(ZipCreate_Destination), nameof(ZipCreateSeparate_Destination), nameof(TarCreate_Destination),
             nameof(ZipCreate_Source), nameof(TarCreate_Source), nameof(SevenZipCreate_Source)],
        ["IArchiveListingRouter.ListEntriesAsync"] = [nameof(RouterList_Archive)],
        ["IRecoveryService.HasFilesFor"] = [nameof(FindArchive_Par2)],
        ["IRecoveryService.FindArchive"] = [nameof(FindArchive_Par2)],
        ["IRecoveryService.RepairAsync"] = [nameof(Repair_Par2), nameof(Repair_Output)],
        ["IAntivirusScanService.ScanAsync"] = [nameof(Scan_Archive)],
        ["FileHashService.ComputeAsync"] = [nameof(Hash_Path)],
    };

    // Public members with no path to be hostile about.
    private static readonly Dictionary<string, string> NotPathOperations = new()
    {
        ["ITarService.DetectCapabilitiesAsync"] = "runs tar.exe --version; takes no path",
        ["ITarService.CanCreateSevenZipAsync"] = "probes tar.exe's 7z writer in its own temp folder; takes no path",
        ["ITarService.AskSevenZipWriterAsync"] = "the same probe, keeping 'no answer' apart from 'no'; takes no path",
        ["FileHashService.ComputeStreamDigestAsync"] = "hashes a stream the caller opened",
    };

    [Fact]
    public void CompletenessTable_MatchesThePublicOperations()
    {
        Type[] types =
        [
            typeof(IArchiveService), typeof(ITarService), typeof(IExtractionRouter), typeof(IArchiveCreationRouter),
            typeof(IArchiveListingRouter), typeof(IRecoveryService), typeof(IAntivirusScanService), typeof(FileHashService),
        ];
        HashSet<string> operations = [.. types.SelectMany(type => type
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => type.Name + "." + method.Name))];

        operations.Should().BeEquivalentTo(Coverage.Keys.Concat(NotPathOperations.Keys));
        HashSet<string> tests = [.. typeof(CoreNeverThrowsContractTests).GetMethods().Select(method => method.Name)];
        Coverage.Values.SelectMany(names => names).Should().OnlyContain(name => tests.Contains(name));
    }

    private async Task<string> TarGz()
    {
        string destination = Destination();
        ArchiveResult made = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Source()], DestinationFolder = destination, ArchiveName = "fixture", Format = ArchiveContainerFormat.TarGz,
        });
        return made.CreatedFiles.Single();
    }

    private async Task<string> Par2()
    {
        ArchiveResult made = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Source()], DestinationFolder = Destination(), ArchiveName = "fixture", RecoveryPercent = 5,
        });
        return made.RecoveryFiles[0];
    }
}
