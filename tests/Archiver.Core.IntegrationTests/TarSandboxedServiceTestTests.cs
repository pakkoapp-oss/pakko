using System.Diagnostics;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F397: "Test" for the archives tar.exe reads, through the real tar.exe in the sandbox
/// ("-x -O": every entry is read, nothing is written). What tar.exe verifies was measured first
/// (docs/DECISIONS.md's T-F397 entry): it checks 7z, xz, bzip2 and zstd contents and does not
/// check gzip's CRC-32, a RAR's data, a plain tar's or an .lzma's. For those a passed test says
/// that only the structure was checked.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceTestTests : IDisposable
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures");

    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Work
    {
        get
        {
            string work = Path.Combine(_temp.Path, "work");
            Directory.CreateDirectory(work);
            return work;
        }
    }

    // A tree with compressible text, incompressible bytes, an empty file, a folder and a
    // non-ASCII name the ANSI code page carries.
    private string BuildTree()
    {
        string tree = Path.Combine(_temp.Path, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        File.WriteAllText(Path.Combine(tree, "text.txt"), string.Concat(Enumerable.Repeat("The quick brown fox. ", 10_000)));
        File.WriteAllBytes(Path.Combine(tree, "sub", "data.bin"), RandomBytes(300_000, seed: 397));
        File.WriteAllBytes(Path.Combine(tree, "empty.txt"), []);
        if (TarCodePage.PortableNonAsciiName() is { } name)
            File.WriteAllText(Path.Combine(tree, "sub", name), "non-ASCII name");
        return tree;
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private string Create(string name, params string[] tarSwitches)
    {
        BuildTree();
        string archive = Path.Combine(Work, name);
        var start = new ProcessStartInfo(TarExecutablePath) { UseShellExecute = false, RedirectStandardError = true };
        foreach (string argument in (string[])[.. tarSwitches, archive, "-C", _temp.Path, "tree"])
            start.ArgumentList.Add(argument);
        using Process tar = Process.Start(start)!;
        string stdErr = tar.StandardError.ReadToEnd();
        tar.WaitForExit();
        tar.ExitCode.Should().Be(0, stdErr);
        return archive;
    }

    private string Create(string kind) => kind switch
    {
        "tar" => Create("a.tar", "-cf"),
        "gz" => Create("a.tar.gz", "-czf"),
        "bz2" => Create("a.tar.bz2", "-cjf"),
        "xz" => Create("a.tar.xz", "-cJf"),
        "zst" => Create("a.tar.zst", "--zstd", "-cf"),
        "lzma" => Create("a.tar.lzma", "--lzma", "-cf"),
        "7z-lzma2" => Create("a.7z", "--format=7zip", "--options", "7zip:compression=lzma2", "-cf"),
        "7z-copy" => Create("a.7z", "--format=7zip", "--options", "7zip:compression=copy", "-cf"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void Flip(string path, double fraction)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bytes[(int)(bytes.Length * fraction)] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
    }

    private static void Truncate(string path, double fraction)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
        stream.SetLength((long)(stream.Length * fraction));
    }

    private static string Fixture(string name) => Path.GetFullPath(Path.Combine(Fixtures, name));

    private static void ShouldHavePassed(ArchiveResult result, string archive, bool contentsVerified)
    {
        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        result.Sources.Should().ContainSingle().Which.Should().BeEquivalentTo(new SourceResult { Path = archive, Outcome = SourceOutcome.Completed });
        if (contentsVerified)
            result.Warnings.Should().BeEmpty();
        else
            result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TestedStructureOnly);
    }

    private static void ShouldHaveFailed(ArchiveResult result, string archive)
    {
        result.Errors.Should().ContainSingle().Which.SourcePath.Should().Be(archive);
        result.Warnings.Should().BeEmpty("a failed test says nothing of what a passed one would have checked");
        result.Sources.Should().ContainSingle().Which.Outcome.Should().NotBe(SourceOutcome.Completed);
    }

    // ── A whole archive ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("xz", true)]
    [InlineData("bz2", true)]
    [InlineData("zst", true)]
    [InlineData("tar", false)]
    [InlineData("gz", false)]
    [InlineData("lzma", false)]
    public async Task TestAsync_WholeArchive_Passes_AndSaysWhenOnlyTheStructureWasChecked(string kind, bool contentsVerified)
    {
        string archive = Create(kind);

        ArchiveResult result = await _sut.TestAsync([archive]);

        ShouldHavePassed(result, archive, contentsVerified);
    }

    [SkipIfFormatUnsupported("7z")]
    public async Task TestAsync_SevenZipFixture_Passes()
    {
        string archive = Fixture("valid.7z");

        ShouldHavePassed(await _sut.TestAsync([archive]), archive, contentsVerified: true);
    }

    [SkipIfFormatUnsupported("rar")]
    public async Task TestAsync_RarFixture_PassesWithTheStructureOnlyWarning()
    {
        string archive = Fixture("valid.rar");

        ShouldHavePassed(await _sut.TestAsync([archive]), archive, contentsVerified: false);
    }

    [Integration]
    public async Task TestAsync_WritesNothingNextToTheArchive()
    {
        string archive = Create("xz");

        await _sut.TestAsync([archive]);

        Directory.GetFileSystemEntries(Work).Should().Equal(archive);
    }

    // ── Damage ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("xz", 0.25)]
    [InlineData("xz", 0.75)]
    [InlineData("bz2", 0.5)]
    [InlineData("zst", 0.5)]
    public async Task TestAsync_FlippedByteInACheckedFormat_Fails(string kind, double where)
    {
        string archive = Create(kind);
        Flip(archive, where);

        ShouldHaveFailed(await _sut.TestAsync([archive]), archive);
    }

    [SevenZipWriterTheory]
    [InlineData("7z-lzma2", 0.25)]
    [InlineData("7z-lzma2", 0.5)]
    [InlineData("7z-lzma2", 0.75)]
    [InlineData("7z-copy", 0.25)]
    [InlineData("7z-copy", 0.75)]
    public async Task TestAsync_FlippedByteInASevenZip_Fails(string kind, double where)
    {
        string archive = Create(kind);
        Flip(archive, where);

        ShouldHaveFailed(await _sut.TestAsync([archive]), archive);
    }

    [SevenZipWriterTheory]
    [InlineData("7z-lzma2")]
    [InlineData("7z-copy")]
    public async Task TestAsync_SevenZipPakkoCouldHaveMade_Passes(string kind)
    {
        string archive = Create(kind);

        ShouldHavePassed(await _sut.TestAsync([archive]), archive, contentsVerified: true);
    }

    [Theory]
    [InlineData("tar")]
    [InlineData("gz")]
    [InlineData("xz")]
    [InlineData("lzma")]
    public async Task TestAsync_TruncatedArchive_Fails(string kind)
    {
        string archive = Create(kind);
        Truncate(archive, 0.6);

        ShouldHaveFailed(await _sut.TestAsync([archive]), archive);
    }

    [SevenZipWriterFact]
    public async Task TestAsync_TruncatedSevenZip_Fails()
    {
        string archive = Create("7z-lzma2");
        Truncate(archive, 0.6);

        ShouldHaveFailed(await _sut.TestAsync([archive]), archive);
    }

    // What the warning is for: tar.exe reads a gzip with a changed byte to its end and exits 0
    // (it does not compare gzip's CRC-32), and a plain tar has no checksum of contents at all.
    // If a later tar.exe starts catching these, the warning for that format can go.
    [Theory]
    [InlineData("gz")]
    [InlineData("tar")]
    public async Task TestAsync_FlippedContentByteInAnUncheckedFormat_PassesWithTheWarning(string kind)
    {
        string archive = Create(kind);
        Flip(archive, 0.5);

        ShouldHavePassed(await _sut.TestAsync([archive]), archive, contentsVerified: false);
    }

    // A zstd frame without the content checksum (the flag is bit 2 of the byte after the magic,
    // the checksum the frame's last four bytes): nothing verifies what it holds.
    [Integration]
    public async Task TestAsync_ZstdWithoutTheContentChecksum_PassesWithTheWarning()
    {
        string archive = Create("zst");
        byte[] bytes = File.ReadAllBytes(archive);
        (bytes[4] & 0x04).Should().NotBe(0, "tar.exe writes the checksum, which is what the other tests rely on");
        bytes[4] &= 0xFB;
        File.WriteAllBytes(archive, bytes[..^4]);

        ShouldHavePassed(await _sut.TestAsync([archive]), archive, contentsVerified: false);
    }

    // ── Refusals ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("encrypted.7z")]
    [InlineData("encrypted_headers.7z")]
    [InlineData("encrypted.rar")]
    [InlineData("encrypted_headers.rar")]
    public async Task TestAsync_EncryptedArchive_SaysPasswordsAreNotSupported(string fixture)
    {
        string archive = Fixture(fixture);

        ArchiveResult result = await _sut.TestAsync([archive]);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.PasswordProtectedFormatNotSupported);
        result.Warnings.Should().BeEmpty();
    }

    // Extraction refuses the whole archive for one such name (T-F49); a test that passed it would
    // promise an extraction that cannot happen.
    [Integration]
    public async Task TestAsync_EntryNameThatEscapes_IsRefusedAsExtractionRefusesIt()
    {
        string archive = Path.Combine(Work, "escape.tar");
        TarBuilder.WriteTar(archive, [new TarBuilder.Entry { Name = "../outside.txt", Content = "x"u8.ToArray() }]);

        ArchiveResult result = await _sut.TestAsync([archive]);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarUnsafeEntryPath);
    }

    [Integration]
    public async Task TestAsync_LinkEntry_IsRefusedAsExtractionRefusesIt()
    {
        string archive = Path.Combine(Work, "link.tar");
        TarBuilder.WriteTar(archive, [new TarBuilder.Entry { Name = "link", TypeFlag = '2', LinkName = "target" }]);

        ArchiveResult result = await _sut.TestAsync([archive]);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarSpecialEntry);
    }

    [Integration]
    public async Task TestAsync_NotAnArchive_Fails()
    {
        string archive = Path.Combine(Work, "notes.tar");
        File.WriteAllText(archive, "these are notes, not a tar");

        ShouldHaveFailed(await _sut.TestAsync([archive]), archive);
    }

    [Fact]
    public async Task TestAsync_MissingFile_SaysSo()
    {
        string archive = Path.Combine(Work, "absent.7z");

        ArchiveResult result = await _sut.TestAsync([archive]);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNotFound);
        result.Sources.Should().BeEmpty("as the ZIP engine's test: a file that is not there was not looked at");
    }

    [Fact]
    public async Task TestAsync_TarDisabledByPolicy_RefusesWithoutRunningTar()
    {
        string archive = Fixture("valid.7z");
        var sut = new TarSandboxedService(new GroupPolicyOptions { DisableTarExtraction = true });

        ArchiveResult result = await sut.TestAsync([archive]);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarExtractionDisabled);
        result.Sources.Should().ContainSingle().Which.Outcome.Should().Be(SourceOutcome.NotProcessed);
    }

    [Fact]
    public async Task TestAsync_NoArchives_IsAnEmptyResult()
    {
        ArchiveResult result = await _sut.TestAsync([]);

        result.Errors.Should().BeEmpty();
        result.Sources.Should().BeEmpty();
    }

    // ── Several archives, progress, cancellation ────────────────────────────────────────────────

    [Integration]
    public async Task TestAsync_OneDamagedOfTwo_ReportsEachByItself()
    {
        string good = Create("xz");
        string bad = Path.Combine(Work, "bad.tar.xz");
        File.Copy(good, bad);
        Flip(bad, 0.5);

        ArchiveResult result = await _sut.TestAsync([good, bad]);

        result.Errors.Should().ContainSingle().Which.SourcePath.Should().Be(bad);
        result.Sources.Select(s => (s.Path, s.Outcome)).Should().Equal(
            (good, SourceOutcome.Completed), (bad, SourceOutcome.Partial));
    }

    [Integration]
    public async Task TestAsync_OneArchive_ReportsBytesReadAgainstTheDeclaredSize()
    {
        string archive = Create("xz");
        var reports = new RecordingProgress();

        await _sut.TestAsync([archive], reports);

        reports.Seen.Should().NotBeEmpty();
        reports.Seen.Select(r => r.Percent).Should().BeInAscendingOrder();
        ProgressReport last = reports.Seen[^1];
        last.Percent.Should().Be(100);
        last.TotalBytes.Should().BeGreaterThan(300_000, "the declared size of the tree's files");
        last.BytesTransferred.Should().Be(last.TotalBytes);
    }

    [Integration]
    public async Task TestAsync_SeveralArchives_ReportsOneStepForEach()
    {
        string first = Create("xz");
        string second = Path.Combine(Work, "second.tar.xz");
        File.Copy(first, second);
        var reports = new RecordingProgress();

        await _sut.TestAsync([first, second], reports);

        reports.Seen.Select(r => r.Percent).Should().Equal(50, 100);
    }

    [Fact]
    public async Task TestAsync_CancelledBeforeItStarts_Throws()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Func<Task> test = () => _sut.TestAsync([Fixture("valid.7z")], cancellationToken: cancel.Token);

        await test.Should().ThrowAsync<OperationCanceledException>();
    }

    [Integration]
    public async Task TestAsync_CancelledWhileTarReads_ThrowsAndLeavesNothing()
    {
        string archive = Create("gz");
        using var cancel = new CancellationTokenSource();
        var reports = new RecordingProgress(_ => cancel.Cancel());

        Func<Task> test = () => _sut.TestAsync([archive], reports, cancel.Token);

        await test.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFileSystemEntries(Work).Should().Equal(archive);
    }

    // Synchronous on purpose: System.Progress posts to the thread pool, and a test that waits on
    // it can time out on a loaded runner (T-F162).
    private sealed class RecordingProgress(Action<ProgressReport>? onReport = null) : IProgress<ProgressReport>
    {
        public List<ProgressReport> Seen { get; } = [];

        public void Report(ProgressReport value)
        {
            Seen.Add(value);
            onReport?.Invoke(value);
        }
    }
}

/// <summary>A theory that needs a tar.exe with the 7z writer; see <see cref="SevenZipWriterFactAttribute"/>.</summary>
public sealed class SevenZipWriterTheoryAttribute : TheoryAttribute
{
    public SevenZipWriterTheoryAttribute()
    {
        if (!new TarSandboxedService(new GroupPolicyOptions()).CanCreateSevenZipAsync().GetAwaiter().GetResult())
            Skip = "this system's tar.exe does not write 7z";
    }
}
