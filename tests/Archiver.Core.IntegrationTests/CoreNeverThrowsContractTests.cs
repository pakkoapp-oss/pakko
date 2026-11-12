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
        // T-F509: "twin-0.zip." can be removed only by its \\?\ path.
        if (_twins.Count > 0 && Directory.Exists(_temp.Path))
            Directory.Delete(@"\\?\" + _temp.Path, recursive: true);
        _temp.Dispose();
    }

    public static TheoryData<string> Hostile =>
    [
        "a file", "a folder", "missing", "a missing drive", "bad characters", "empty", "locked", "not an archive", "an empty file",
        "a device", "a trimmed name",
    ];

    // T-F509: what each "a trimmed name" path's twin was before the operation.
    private readonly List<(string Path, string State)> _twins = [];

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
            // T-F509: "x.zip." beside a twin "x.zip" (a dot: Windows drops it in the middle of a path too) (a real archive, as "a file" is): by its plain
            // path every operation would reach the twin. NoThrow checks the twin is untouched.
            case "a trimmed name":
            {
                string twin = System.IO.Path.Combine(root, "twin-" + _twins.Count + extension);
                if (extension.Length == 0)
                {
                    Directory.CreateDirectory(twin);
                    File.WriteAllText(System.IO.Path.Combine(twin, "a.txt"), "twin");
                    Directory.CreateDirectory(@"\\?\" + twin + ".");
                }
                else
                {
                    string content = System.IO.Path.Combine(root, "twin-content");
                    Directory.CreateDirectory(content);
                    File.WriteAllText(System.IO.Path.Combine(content, "a.txt"), "twin");
                    ZipFile.CreateFromDirectory(content, twin);
                    File.Copy(twin, @"\\?\" + twin + ".");
                }
                _twins.Add((twin, StateOf(twin)));
                return twin + ".";
            }
            // T-F475: NUL, not CON - a console source can wait for input.
            case "a device":
                return @"\\.\NUL";
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

    private async Task NoThrow(Func<Task> operation)
    {
        Exception? escaped = null;
        try { await operation(); }
        catch (Exception ex) { escaped = ex; }
        escaped.Should().BeNull("a Core operation reports errors in its result and never throws");
        foreach ((string twin, string state) in _twins)
            StateOf(twin).Should().Be(state, "the operation was given a name Windows trims, never its twin " + twin);
    }

    // A file's bytes and time; a folder's files, each with its bytes and time.
    private static string StateOf(string path)
    {
        if (File.Exists(path))
            return Convert.ToHexString(File.ReadAllBytes(path)) + "@" + File.GetLastWriteTimeUtc(path).Ticks;
        if (!Directory.Exists(path))
            return "gone";
        return string.Join("|", Directory.GetFileSystemEntries(path, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(entry => entry + "=" + StateOf(entry)));
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

    // T-F420: where a source path leads, and what it holds (-sdel and "Delete after"); T-F511: the
    // key a frontend matches it by.
    [Theory, MemberData(nameof(Hostile))]
    public Task PathIdentity_Path(string kind) => NoThrow(() =>
    {
        string path = Path(kind);
        _ = Archiver.Core.IO.PathIdentity.Resolve(path);
        _ = Archiver.Core.IO.PathIdentity.SelfAndAncestors(path);
        _ = Archiver.Core.IO.PathIdentity.FinalPathOfItself(path);
        _ = SourceKey.Of(path);
        return Task.CompletedTask;
    });

    // T-F511: the removal and sweep of Pakko's temp names, which the CLI and the App call.
    [Theory, MemberData(nameof(Hostile))]
    public Task TempOwner_Path(string kind) => NoThrow(() =>
    {
        string path = Path(kind);
        Archiver.Core.IO.TempOwner.SweepStaleInTemp(path, "", TimeSpan.FromDays(1));
        _ = Archiver.Core.IO.TempOwner.TryRemove(path);
        return Task.CompletedTask;
    });

    [Theory, MemberData(nameof(Hostile))]
    public Task SourceTree_Path(string kind) => NoThrow(() =>
    {
        _ = Archiver.Core.IO.SourceTree.List(Path(kind));
        return Task.CompletedTask;
    });

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

    // T-F414: a source named by part of its path.
    [Theory, MemberData(nameof(Hostile))]
    public Task ZipEngineCreate_SourceWithNameParts(string kind) => NoThrow(() =>
        _services.ArchiveService.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Path(kind, ".txt")], SourceNameParts = [2], DestinationFolder = Destination(), ArchiveName = "out",
        }));

    [Theory, MemberData(nameof(Hostile))]
    public Task TarEngineCompress_SourceWithNameParts(string kind) => NoThrow(() =>
        _services.TarService.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [Path(kind, ".txt")], SourceNameParts = [2], DestinationFolder = Destination(), ArchiveName = "out",
            Format = ArchiveContainerFormat.TarGz,
        }));

    // T-F419: the archive's own exact name (-sae): a device name took the engine down.
    public static TheoryData<string> DeviceNames => ["NUL", "CON ", "aux", "COM1.", "nul.", "CONOUT$"];

    [Theory, MemberData(nameof(DeviceNames))]
    public Task ZipEngineCreate_ExactName(string name) => NoThrow(() =>
        _services.ArchiveService.ArchiveAsync(new ArchiveOptions { SourcePaths = [Source()], DestinationFolder = Destination(), ExactFileName = name }));

    [Theory, MemberData(nameof(DeviceNames))]
    public Task TarEngineCompress_ExactName(string name) => NoThrow(() =>
        _services.TarService.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [Source()], DestinationFolder = Destination(), ExactFileName = name, Format = ArchiveContainerFormat.TarGz,
        }));

    [Theory, MemberData(nameof(DeviceNames))]
    public async Task Create_ExactDeviceName_RefusedWithItsOwnCode_NothingWritten(string name)
    {
        string destination = Destination();
        foreach (ArchiveContainerFormat format in new[] { ArchiveContainerFormat.Zip, ArchiveContainerFormat.TarGz })
        {
            ArchiveResult result = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [Source()], DestinationFolder = destination, ExactFileName = name, Format = format, RecoveryPercent = 5,
            });

            result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.ArchiveNameReserved);
            result.CreatedFiles.Should().BeEmpty();
        }
        Directory.Exists(destination).Should().BeFalse("nothing is written, not even the folder");
    }

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

    // T-F475: an archive path that is a device itself (no folder above it: ArchiveTempFile's folder
    // was null and the engine threw) or lies under a device's root (the archive went to the device
    // and the run reported success). On Windows 10 and Server 2022 "CON.zip" in any folder is the
    // first kind; these reach both kinds on any Windows.
    public static TheoryData<string> DevicePaths => [@"\\.\NUL", @"\\.\CON", @"\\.\NUL\o.zip", @"\\?\nul\o.zip"];

    [Theory, MemberData(nameof(DevicePaths))]
    public Task ZipEngineCreate_DevicePath(string path) => NoThrow(() =>
        _services.ArchiveService.ArchiveAsync(new ArchiveOptions { SourcePaths = [Source()], DestinationFolder = Destination(), ExactFileName = path }));

    [Theory, MemberData(nameof(DevicePaths))]
    public Task TarEngineCompress_DevicePath(string path) => NoThrow(() =>
        _services.TarService.CompressAsync(new ArchiveOptions
        {
            SourcePaths = [Source()], DestinationFolder = Destination(), ExactFileName = path, Format = ArchiveContainerFormat.TarGz,
        }));

    [Theory, MemberData(nameof(DevicePaths))]
    public async Task Create_DevicePath_RefusedWithItsOwnCode_NothingWritten(string path)
    {
        string destination = Destination();
        foreach (ArchiveContainerFormat format in new[] { ArchiveContainerFormat.Zip, ArchiveContainerFormat.TarGz })
        {
            ArchiveResult result = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
            {
                SourcePaths = [Source()], DestinationFolder = destination, ExactFileName = path, Format = format,
            });

            result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.ArchiveNameReserved);
            result.CreatedFiles.Should().BeEmpty();
        }
        Directory.Exists(destination).Should().BeFalse("nothing is written, not even the folder");
    }

    [Theory, MemberData(nameof(Hostile))]
    public Task TarCreate_Destination(string kind) => NoThrow(() =>
        _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Source()], DestinationFolder = Path(kind, ""), ArchiveName = "out", Format = ArchiveContainerFormat.TarGz,
        }));

    // T-F511: a set that needs repair, or nothing is written to the folder and the row checks nothing.
    [Theory, MemberData(nameof(Hostile))]
    public async Task Repair_Output(string kind)
    {
        ArchiveResult? repaired = null;
        await NoThrow(async () =>
            repaired = await _services.RecoveryService.RepairAsync(new RepairOptions { Paths = [await DamagedPar2()], OutputDirectory = Path(kind, "") }));
        if (kind == "a folder")
            repaired!.CreatedFiles.Should().ContainSingle("the damaged archive is repaired into a folder that can be used");
    }

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

    // T-F475: where the folder is the device, the error is the folder's, not one about a fine name.
    [Theory]
    [InlineData(ArchiveContainerFormat.Zip)]
    [InlineData(ArchiveContainerFormat.TarGz)]
    public async Task Create_DeviceFolder_IsTheFoldersError(ArchiveContainerFormat format)
    {
        ArchiveResult result = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [Source()], DestinationFolder = @"\\.\NUL", ArchiveName = "out", Format = format,
        });

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.CannotCreateArchive);
        result.CreatedFiles.Should().BeEmpty();
    }

    // --- Completeness: every public operation has rows above -------------------------------

    // (interface or class, method) -> the tests above that cover it. A new public method on one of
    // these types fails CompletenessTable_MatchesThePublicOperations until it gets rows here.
    private static readonly Dictionary<string, string[]> Coverage = new()
    {
        ["IArchiveService.ArchiveAsync"] = [nameof(ZipEngineCreate_Destination), nameof(ZipEngineCreate_Source), nameof(ZipEngineCreate_DevicePath)],
        ["IArchiveService.ExtractAsync"] = [nameof(ZipExtract_Archive), nameof(ZipExtract_Destination)],
        ["IArchiveService.TestAsync"] = [nameof(ZipTest_Archive)],
        ["IArchiveService.ListEntriesAsync"] = [nameof(ZipList_Archive)],
        ["ITarService.ExtractAsync"] = [nameof(TarExtract_Archive), nameof(TarExtract_Destination)],
        ["ITarService.TestAsync"] = [nameof(TarTest_Archive)],
        ["ITarService.ListEntriesAsync"] = [nameof(TarList_Archive)],
        ["ITarService.CompressAsync"] = [nameof(TarEngineCompress_Destination), nameof(TarEngineCompress_Source), nameof(TarEngineCompress_DevicePath)],
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
        ["PathIdentity.Resolve"] = [nameof(PathIdentity_Path)],
        ["PathIdentity.SelfAndAncestors"] = [nameof(PathIdentity_Path)],
        ["PathIdentity.FinalPathOfItself"] = [nameof(PathIdentity_Path)],
        ["SourceKey.Of"] = [nameof(PathIdentity_Path)],
        ["TempOwner.SweepStaleInTemp"] = [nameof(TempOwner_Path)],
        ["TempOwner.TryRemove"] = [nameof(TempOwner_Path)],
        ["SourceTree.List"] = [nameof(SourceTree_Path)],
    };

    // Public members with no path to be hostile about.
    private static readonly Dictionary<string, string> NotPathOperations = new()
    {
        ["ITarService.DetectCapabilitiesAsync"] = "runs tar.exe --version; takes no path",
        ["ITarService.KnownCapabilitiesAsync"] = "the same probe, asked again with more patience; takes no path",
        ["ITarService.CanCreateSevenZipAsync"] = "probes tar.exe's 7z writer in its own temp folder; takes no path",
        ["ITarService.AskSevenZipWriterAsync"] = "the same probe, keeping 'no answer' apart from 'no'; takes no path",
        ["FileHashService.ComputeStreamDigestAsync"] = "hashes a stream the caller opened",
        ["EntryDiskName.AsPakkoWrites"] = "a name inside an archive, never opened",
        ["EntryDiskName.AsTarExeWrites"] = "a name inside an archive, never opened",
        ["EntryDiskName.Renames"] = "a name inside an archive, never opened",
        ["EntryDiskName.SingleName"] = "one name Pakko writes, never opened",
        ["InputPaths.NameTrimmed"] = "a question about a path's spelling, never opened",
        ["TempOwner.NewName"] = "a new name, never opened",
        ["TempOwner.IsRunningHere"] = "a question about a process, never a path",
        ["EntryDiskName.SameName"] = "names inside an archive, never opened",
        ["EntryDiskName.TrimmedTwins"] = "names inside an archive, never opened",
        ["EntryDiskName.Trims"] = "a name inside an archive, never opened",
        ["EntryDiskName.ShortNameAliasOfAnother"] = "names inside an archive, never opened",
        ["EntryDiskName.StripLeadingDotSlash"] = "a name inside an archive, never opened",
    };

    [Fact]
    public void CompletenessTable_MatchesThePublicOperations()
    {
        Type[] types =
        [
            typeof(IArchiveService), typeof(ITarService), typeof(IExtractionRouter), typeof(IArchiveCreationRouter),
            typeof(IArchiveListingRouter), typeof(IRecoveryService), typeof(IAntivirusScanService), typeof(FileHashService),
            typeof(Archiver.Core.IO.PathIdentity), typeof(Archiver.Core.IO.SourceTree), typeof(EntryDiskName),
            typeof(InputPaths), typeof(SourceKey), typeof(Archiver.Core.IO.TempOwner),
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

    // An archive whose data is damaged, beside its set.
    private async Task<string> DamagedPar2()
    {
        string content = System.IO.Path.Combine(_temp.Path, "damaged-content");
        Directory.CreateDirectory(content);
        File.WriteAllText(System.IO.Path.Combine(content, "a.txt"), string.Concat(Enumerable.Repeat("recoverable line\n", 4000)));
        string destination = Destination();
        ArchiveResult made = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [content], DestinationFolder = destination, ArchiveName = "damaged", RecoveryPercent = 10,
        });
        TrimmedSourceNameTests.DamageData(System.IO.Path.Combine(destination, "damaged.zip"));
        return made.RecoveryFiles[0];
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
