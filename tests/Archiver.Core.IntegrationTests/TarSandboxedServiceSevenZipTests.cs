using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// A test that needs a tar.exe with the 7z writer. Skipped where this system's tar.exe has none
/// (Pakko then refuses 7z creation, which Archiver.Core.Tests covers).
/// </summary>
public sealed class SevenZipWriterFactAttribute : FactAttribute
{
    public SevenZipWriterFactAttribute()
    {
        if (!new TarSandboxedService(new GroupPolicyOptions()).CanCreateSevenZipAsync().GetAwaiter().GetResult())
            Skip = "this system's tar.exe does not write 7z";
    }
}

/// <summary>
/// T-F342 (Core): 7z creation through the real tar.exe, read back by Pakko's own sandboxed reader.
/// 7-Zip's verdict on the same archives is in Archiver.Core.PerformanceTests
/// (SevenZipCreationCompatibilityTests), where the vendored 7za.exe is.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceSevenZipTests : IDisposable
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";
    private static readonly byte[] Signature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Out => Path.Combine(_temp.Path, "out");

    private string BuildTree(string? nonAsciiName = null)
    {
        string tree = Path.Combine(_temp.Path, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        Directory.CreateDirectory(Path.Combine(tree, "empty folder"));
        File.WriteAllText(Path.Combine(tree, "text.txt"), string.Concat(Enumerable.Repeat("text ", 5_000)));
        File.WriteAllBytes(Path.Combine(tree, "sub", "data.bin"), RandomBytes(100_000, seed: 3));
        File.WriteAllBytes(Path.Combine(tree, "empty.txt"), []);
        if (nonAsciiName is not null)
            File.WriteAllText(Path.Combine(tree, "sub", nonAsciiName), "non-ASCII name");
        return tree;
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private ArchiveOptions Options(params string[] sources) => new()
    {
        SourcePaths = sources,
        DestinationFolder = Out,
        ArchiveName = "out",
        Format = ArchiveContainerFormat.SevenZip,
    };

    private static Dictionary<string, byte[]> Files(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);

    private async Task<List<string>> EntryNamesAsync(string archive)
    {
        ArchiveListResult listed = await _sut.ListEntriesAsync(archive);
        listed.Success.Should().BeTrue(because: listed.ErrorMessage);
        return [.. listed.Entries.Select(entry => entry.Path.TrimEnd('/'))];
    }

    private static bool IsSevenZip(string path) => File.ReadAllBytes(path).AsSpan(0, 6).SequenceEqual(Signature);

    // --- Happy path ---

    [SevenZipWriterFact]
    public async Task ProbeSevenZipWriterAsync_OnThisSystem_FindsTheWriter() =>
        (await TarSandboxedService.ProbeSevenZipWriterAsync(TarExecutablePath)).Should().BeTrue();

    [SevenZipWriterFact]
    public async Task CompressAsync_Tree_RoundTripsThroughPakkosOwnReader()
    {
        string tree = BuildTree(TarCodePage.PortableNonAsciiName());

        ArchiveResult result = await _sut.CompressAsync(Options(tree));

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        string archive = result.CreatedFiles.Should().ContainSingle().Which;
        archive.Should().Be(Path.Combine(Out, "out.7z"));
        IsSevenZip(archive).Should().BeTrue();
        Directory.GetFileSystemEntries(Out).Should().Equal(archive);

        string extracted = Path.Combine(_temp.Path, "extracted");
        ArchiveResult extraction = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive], DestinationFolder = extracted, OpenDestinationFolder = false,
        });
        extraction.Success.Should().BeTrue(because: string.Join("; ", extraction.Errors.Select(e => e.Message)));
        // Wherever the single root folder landed: the folder that holds the tree's top file.
        string extractedTree = Path.GetDirectoryName(Directory.GetFiles(extracted, "text.txt", SearchOption.AllDirectories).Single())!;
        Files(extractedTree).Should().BeEquivalentTo(Files(tree));
        Directory.Exists(Path.Combine(extractedTree, "empty folder")).Should().BeTrue();
    }

    [SevenZipWriterFact]
    public async Task CompressAsync_SeparateArchives_WritesOneSevenZipForEachSource()
    {
        string tree = BuildTree();
        string file = Path.Combine(_temp.Path, "single.txt");
        File.WriteAllText(file, "single");

        ArchiveResult result = await _sut.CompressAsync(Options(tree, file) with { Mode = ArchiveMode.SeparateArchives, ArchiveName = null });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        result.CreatedFiles.Select(Path.GetFileName).Should().BeEquivalentTo("tree.7z", "single.7z");
        result.CreatedFiles.Should().OnlyContain(path => IsSevenZip(path));
    }

    [SevenZipWriterFact]
    public async Task ArchiveCreationRouter_SevenZipWithRecoveryData_WritesASetNextToIt()
    {
        var router = new ArchiveCreationRouter(new ZipArchiveService(new GroupPolicyOptions()), _sut, new GroupPolicyOptions());

        ArchiveResult result = await router.ArchiveAsync(Options(BuildTree()) with { RecoveryPercent = 10 });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        result.CreatedFiles.Select(Path.GetFileName).Should().Contain("out.7z");
        Directory.GetFiles(Out, "out.7z*.par2").Should().NotBeEmpty();
    }

    // --- Security & Boundary ---

    [SevenZipWriterFact]
    public async Task CompressAsync_DestinationInsideTheSource_PacksNoFileOfItsOwnAndLeavesNoneBehind()
    {
        string tree = BuildTree();
        string[] before = Directory.GetFileSystemEntries(tree);

        ArchiveResult result = await _sut.CompressAsync(Options(tree) with { DestinationFolder = tree });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        (await EntryNamesAsync(Path.Combine(tree, "out.7z"))).Should().BeEquivalentTo(
            "tree", "tree/empty folder", "tree/empty.txt", "tree/sub", "tree/sub/data.bin", "tree/text.txt");
        Directory.GetFileSystemEntries(tree).Should().BeEquivalentTo([.. before, Path.Combine(tree, "out.7z")]);
    }

    [SevenZipWriterFact]
    public async Task CompressAsync_ArchiveNameWithParentSegments_StaysInTheDestination()
    {
        ArchiveResult result = await _sut.CompressAsync(Options(BuildTree()) with { ArchiveName = @"..\..\escaped" });

        result.CreatedFiles.Should().ContainSingle().Which.Should().Be(Path.Combine(Out, "escaped.7z"));
    }

    [SevenZipWriterFact]
    public async Task CompressAsync_OptionLikeSourceNames_AreArchivedAsFiles()
    {
        string dir = Path.Combine(_temp.Path, "names");
        Directory.CreateDirectory(dir);
        string[] names = ["--format=ustar", "-C", "--passphrase=x", "real.txt"];
        foreach (string name in names)
            File.WriteAllText(Path.Combine(dir, name), name);

        ArchiveResult result = await _sut.CompressAsync(Options([.. names.Select(name => Path.Combine(dir, name))]));

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        IsSevenZip(Path.Combine(Out, "out.7z")).Should().BeTrue();
        (await EntryNamesAsync(Path.Combine(Out, "out.7z"))).Select(name => name.TrimStart('.', '/')).Should().BeEquivalentTo(names);
    }

    [SevenZipWriterFact]
    public async Task CompressAsync_JunctionInsideTheSource_IsLeftOutOfTheSevenZipToo()
    {
        string tree = BuildTree();
        string outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside");
        string link = Path.Combine(tree, "sub", "link");
        DirectoryJunction.Create(link, outside);

        ArchiveResult result = await _sut.CompressAsync(Options(tree));

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle().Which.Path.Should().Be(link);
        (await EntryNamesAsync(Path.Combine(Out, "out.7z"))).Should().NotContain(name => name.Contains("link") || name.Contains("secret"));
    }

    // --- Misuse: the probe runs only for 7z, and once ---

    [SevenZipWriterFact]
    public async Task CompressAsync_OtherFormats_NeverRunTheWriterProbe_AndSevenZipRunsItOnce()
    {
        int probes = 0;
        var sut = new TarSandboxedService(
            new GroupPolicyOptions(),
            () => Task.FromResult(new TarCapabilities { Supports7z = true }),
            sevenZipWriterProbe: () => { probes++; return TarSandboxedService.ProbeSevenZipWriterAsync(TarExecutablePath); });
        string tree = BuildTree();

        (await sut.CompressAsync(Options(tree) with { Format = ArchiveContainerFormat.TarGz })).Success.Should().BeTrue();
        probes.Should().Be(0);

        (await sut.CompressAsync(Options(tree) with { ArchiveName = "first" })).Success.Should().BeTrue();
        (await sut.CompressAsync(Options(tree) with { ArchiveName = "second" })).Success.Should().BeTrue();
        probes.Should().Be(1);
    }

    [Integration]
    public async Task ProbeSevenZipWriterAsync_NoTarExeAtThePath_IsFalse() =>
        (await TarSandboxedService.ProbeSevenZipWriterAsync(Path.Combine(_temp.Path, "no-tar.exe"))).Should().BeFalse();

    // --- Error path: nothing is left when the run fails or is cancelled ---

    [SevenZipWriterFact]
    public async Task CompressAsync_SourceFileHeldByAnotherProcess_FailsAndLeavesNothing()
    {
        string tree = BuildTree();
        using (new FileStream(Path.Combine(tree, "text.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ArchiveResult result = await _sut.CompressAsync(Options(tree));

            result.CreatedFiles.Should().BeEmpty();
            result.Errors.Should().Contain(e => e.Text!.Code == MessageCode.TarCreationFailed);
        }
        Directory.GetFileSystemEntries(Out).Should().BeEmpty();
    }

    [SevenZipWriterFact]
    public async Task CompressAsync_CancelledWhileTarRuns_ThrowsAndLeavesNothing()
    {
        // Incompressible, so the writer is still busy when the cancel lands.
        string big = Path.Combine(_temp.Path, "big.bin");
        File.WriteAllBytes(big, RandomBytes(48 * 1024 * 1024, seed: 5));
        using var cancellation = new CancellationTokenSource();
        bool writerFileInDestination = false;
        Task watcher = Task.CompletedTask;
        var sut = new TarSandboxedService(
            new GroupPolicyOptions(),
            () => Task.FromResult(new TarCapabilities { Supports7z = true }),
            beforeCreationLaunch: () => watcher = Task.Run(async () =>
            {
                // libarchive's temporary file must appear under the destination, not in %TEMP%.
                for (int i = 0; i < 200 && !writerFileInDestination; i++)
                {
                    await Task.Delay(50);
                    try { writerFileInDestination = Directory.GetFiles(Out, "libarchive_*", SearchOption.AllDirectories).Length > 0; }
                    catch (IOException) { /* the folder is being created or removed */ }
                }
                cancellation.Cancel();
            }));

        Func<Task> act = () => sut.CompressAsync(Options(big), null, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await watcher;
        writerFileInDestination.Should().BeTrue();
        Directory.GetFileSystemEntries(Out).Should().BeEmpty();
    }

    [SevenZipWriterFact]
    public async Task CompressAsync_SourceRemovedJustBeforeTarStarts_IsNamedByPakkoForSevenZipToo()
    {
        string vanishing = Path.Combine(_temp.Path, "vanishing.txt");
        string staying = Path.Combine(_temp.Path, "staying.txt");
        File.WriteAllText(vanishing, "v");
        File.WriteAllText(staying, "s");
        var sut = new TarSandboxedService(
            new GroupPolicyOptions(),
            () => Task.FromResult(new TarCapabilities { Supports7z = true }),
            beforeCreationLaunch: () => File.Delete(vanishing));

        ArchiveResult result = await sut.CompressAsync(Options(vanishing, staying));

        result.CreatedFiles.Should().BeEmpty();
        result.Errors.Should().Contain(e => e.Text!.Code == MessageCode.SourceNotFound && e.SourcePath == vanishing);
        result.Errors.Should().NotContain(e => e.Message.Contains("Couldn't visit directory"));
        Directory.GetFileSystemEntries(Out).Should().BeEmpty();
    }
}
