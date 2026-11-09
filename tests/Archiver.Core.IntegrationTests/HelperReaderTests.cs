using System.Security.Cryptography;
using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Antivirus;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F470: list, extract, test and the antivirus scan through ISandboxedArchiveReader under both
/// backends - tar.exe, and the archive helper over archiveint.dll - on the same archives; then
/// what only the helper does (exact names, every same-named copy) and what it refuses.
/// </summary>
[Collection("TarSandbox")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class HelperReaderTests : IDisposable
{
    public const string TarExe = "tar.exe";
    public const string Helper = "helper";

    public static TheoryData<string> Backends => [TarExe, Helper];

    private static readonly Lazy<HelperSetup> Setup = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // --- Both backends: happy path ---

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Extract_GivesEveryFileAndFolder_WithItsContent(string backend)
    {
        string archive = Tar(("top/a.txt", "A"), ("top/sub/", null), ("top/b.txt", "BB"));

        (ArchiveResult result, string dest) = await ExtractAsync(backend, archive);

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().BeEmpty();
        Tree(dest).Should().BeEquivalentTo(new Dictionary<string, string> { ["top/a.txt"] = "A", ["top/b.txt"] = "BB" });
        Directory.Exists(Path.Combine(dest, "top", "sub")).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Extract_ASelectedFolder_GivesOnlyIt(string backend)
    {
        string archive = Tar(("a/x.txt", "X"), ("b/y.txt", "Y"), ("b/z/w.txt", "W"));

        (ArchiveResult result, string dest) = await ExtractAsync(backend, archive, selected: ["b"]);

        result.Errors.Should().BeEmpty();
        Tree(dest).Should().BeEquivalentTo(new Dictionary<string, string> { ["b/y.txt"] = "Y", ["b/z/w.txt"] = "W" });
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Extract_AFilter_DropsWhatItDoesNotPass(string backend)
    {
        string archive = Tar(("keep.txt", "K"), ("drop.log", "D"));

        (ArchiveResult result, string dest) = await ExtractAsync(backend, archive, filter: (name, _) => name.EndsWith(".txt", StringComparison.Ordinal));

        result.Errors.Should().BeEmpty();
        Tree(dest).Keys.Should().BeEquivalentTo(["keep.txt"]);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Extract_AGzipTar(string backend)
    {
        string archive = Gzip(Tar(("a.txt", "A"), ("b.txt", "B")));

        (ArchiveResult result, string dest) = await ExtractAsync(backend, archive);

        result.Errors.Should().BeEmpty();
        Tree(dest).Keys.Should().BeEquivalentTo(["a.txt", "b.txt"]);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Extract_KeepsTheFolderEntrysTime(string backend)
    {
        string archive = Path.Combine(_temp.Path, "times.tar");
        TarBuilder.WriteTar(archive,
        [
            new TarBuilder.Entry { Name = "d/", TypeFlag = '5', ModifiedUnixSeconds = 1_000_000_000 },
            new TarBuilder.Entry { Name = "d/a.txt", Content = "A"u8.ToArray(), ModifiedUnixSeconds = 1_100_000_000 },
            new TarBuilder.Entry { Name = "e.txt", Content = "E"u8.ToArray(), ModifiedUnixSeconds = 1_100_000_000 },
        ]);

        (ArchiveResult result, string dest) = await ExtractAsync(backend, archive);

        result.Errors.Should().BeEmpty();
        Directory.GetLastWriteTimeUtc(Path.Combine(dest, "d")).Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_000_000_000).UtcDateTime);
        File.GetLastWriteTimeUtc(Path.Combine(dest, "e.txt")).Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_100_000_000).UtcDateTime);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task List_GivesNamesSizesAndFolders(string backend)
    {
        string archive = Tar(("d/", null), ("d/a.txt", "AAA"));

        ArchiveListResult list = await Service(backend).ListEntriesAsync(archive);

        list.Success.Should().BeTrue();
        list.Entries.Select(e => (e.Path, e.Size, e.IsDirectory)).Should().BeEquivalentTo([("d", 0L, true), ("d/a.txt", 3L, false)]);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Test_ASoundArchivePasses_ACutOneFails(string backend)
    {
        string sound = Tar(("a.txt", new string('x', 5000)));
        string cut = Path.Combine(_temp.Path, "cut.tar");
        File.WriteAllBytes(cut, File.ReadAllBytes(sound)[..2048]);

        ArchiveResult good = await Service(backend).TestAsync([sound]);
        ArchiveResult bad = await Service(backend).TestAsync([cut]);

        good.Errors.Should().BeEmpty();
        bad.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task AntivirusScan_NamesAFindingByItsEntry(string backend)
    {
        string archive = Tar(("dir/clean.txt", "clean"), ("dir/bad.txt", "EVIL"));
        var scanner = new FakeAmsiScanner { DetectedText = "EVIL" };

        ThreatScanResult result = await Antivirus(backend, scanner).ScanAsync(new AntivirusScanOptions { ArchivePaths = [archive] });

        result.OverallVerdict.Should().Be(ThreatVerdict.ThreatDetected);
        result.Findings.Should().ContainSingle(f => f.Verdict == ThreatVerdict.ThreatDetected).Which.EntryPath.Should().Be("dir/bad.txt");
        scanner.ScannedContentNames.Should().BeEquivalentTo(["dir/clean.txt", "dir/bad.txt"]);
    }

    // --- Both backends: security & boundary ---

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Extract_ATraversalName_IsRefused_AndNothingLands(string backend)
    {
        string archive = Tar(("ok.txt", "O"), ("../evil.txt", "E"));

        (ArchiveResult result, string dest) = await ExtractAsync(backend, archive);

        result.Errors.Should().NotBeEmpty();
        File.Exists(Path.Combine(_temp.Path, "evil.txt")).Should().BeFalse();
        (Directory.Exists(dest) ? Directory.GetFiles(dest, "*", SearchOption.AllDirectories) : []).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Extract_ASymlink_IsRefused(string backend)
    {
        string archive = Path.Combine(_temp.Path, "link.tar");
        TarBuilder.WriteTar(archive,
        [
            new TarBuilder.Entry { Name = "a.txt", Content = "A"u8.ToArray() },
            new TarBuilder.Entry { Name = "l", TypeFlag = '2', LinkName = "a.txt" },
        ]);

        (ArchiveResult result, _) = await ExtractAsync(backend, archive);

        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Extract_AShortNameAliasOfAnother_IsRefused(string backend)
    {
        string archive = Tar(("longfilename.txt", "L"), ("LONGFI~1.TXT", "S"));

        (ArchiveResult result, _) = await ExtractAsync(backend, archive);

        result.Errors.Should().NotBeEmpty();
    }

    // A name stored as raw bytes in no charset, not UTF-8: read in the page tar.exe reads such names
    // in (libarchive's OEM page), by both backends alike - the helper after its UTF-8 listing failed.
    [Theory]
    [MemberData(nameof(Backends))]
    public async Task List_ARawNameNotUtf8_ReadsInTheRawNamePage(string backend)
    {
        byte[] raw = [0xE0, 0xE1, 0xE2, 0xE3, (byte)'.', (byte)'t', (byte)'x', (byte)'t'];
        string archive = Path.Combine(_temp.Path, "raw.tar");
        TarBuilder.WriteTar(archive, [new TarBuilder.Entry { Name = "x", NameBytes = raw, Content = "R"u8.ToArray() }]);
        string expected = new Archiver.Core.Text.TextBoundary(TarCodePage.RawNameOem).Strict.GetString(raw);
        // tar.exe prints names in the ANSI page: one that page cannot hold lists by best fit there.
        if (backend == TarExe && !RoundTripsThroughAnsi(expected))
            return;

        ArchiveListResult list = await Service(backend).ListEntriesAsync(archive);

        list.Success.Should().BeTrue();
        list.Entries.Select(e => e.Path).Should().Equal(expected);
    }

    // --- The helper only ---

    [Fact]
    public async Task Helper_NamesOutsideTheAnsiPage_LandExactly()
    {
        string[] names = ["日本語.txt", "Ελληνικά.txt", "עברית.txt", "emoji-\U0001F600.txt"];
        string archive = Tar([.. names.Select(n => (n, (string?)n))]);

        (ArchiveResult result, string dest) = await ExtractAsync(Helper, archive);

        result.Errors.Should().BeEmpty();
        Tree(dest).Should().BeEquivalentTo(names.ToDictionary(n => n, n => n));
    }

    [Theory]
    [InlineData(ConflictBehavior.Rename, "dup.txt=first", "dup (1).txt=second")]
    [InlineData(ConflictBehavior.Overwrite, "dup.txt=second")]
    [InlineData(ConflictBehavior.Skip, "dup.txt=first")]
    public async Task Helper_SameNamedCopies_EachMeetTheConflictRule_InArchiveOrder(ConflictBehavior rule, params string[] expected)
    {
        string archive = Tar(("dup.txt", "first"), ("dup.txt", "second"));

        (ArchiveResult result, string dest) = await ExtractAsync(Helper, archive, onConflict: rule);

        result.Errors.Should().BeEmpty();
        Tree(dest).Should().BeEquivalentTo(expected.Select(e => e.Split('=')).ToDictionary(p => p[0], p => p[1]));
    }

    [Fact]
    public async Task Helper_ATrailingDot_IsTrimmed_AndDoesNotReplaceTheFileItLandsOn()
    {
        string archive = Tar(("a.txt", "plain"), ("a.txt.", "dotted"));

        (ArchiveResult result, string dest) = await ExtractAsync(Helper, archive, onConflict: ConflictBehavior.Overwrite);

        result.Errors.Should().BeEmpty();
        Tree(dest).Should().BeEquivalentTo(new Dictionary<string, string> { ["a.txt"] = "plain", ["a (1).txt"] = "dotted" });
    }

    // T-F420 review: a trimmed name does not name the file already at the destination it lands on.
    [Fact]
    public async Task Helper_ATrailingDot_DoesNotReplaceAFileAlreadyThere()
    {
        string archive = Tar(("a.txt.", "dotted"));
        string dest = Path.Combine(_temp.Path, "dest-existing");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "a.txt"), "mine");

        ArchiveResult result = await Service(Helper).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            OnConflict = ConflictBehavior.Overwrite,
        });

        result.Errors.Should().BeEmpty();
        Tree(dest).Should().BeEquivalentTo(new Dictionary<string, string> { ["a.txt"] = "mine", ["a (1).txt"] = "dotted" });
    }

    [Fact]
    public async Task Helper_ContentIsByteExact()
    {
        byte[] data = RandomNumberGenerator.GetBytes(300_000);
        string archive = Path.Combine(_temp.Path, "bytes.tar");
        TarBuilder.WriteTar(archive, [new TarBuilder.Entry { Name = "r.bin", Content = data }]);

        (ArchiveResult result, string dest) = await ExtractAsync(Helper, archive);

        result.Errors.Should().BeEmpty();
        SHA256.HashData(File.ReadAllBytes(Path.Combine(dest, "r.bin"))).Should().Equal(SHA256.HashData(data));
    }

    [Fact]
    public async Task Helper_AnExtractionGoesThroughTheHelper_AndAZipInAGzipThroughTarExe()
    {
        string tar = Gzip(Tar(("a.txt", "A")));
        string zipInGzip = Path.Combine(_temp.Path, "z.gz");
        string zip = Path.Combine(_temp.Path, "inner.zip");
        using (System.IO.Compression.ZipArchive z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            z.CreateEntry("a.txt");
        File.Move(Gzip(zip), zipInGzip);

        using (ISandboxedArchiveReader reader = await ISandboxedArchiveReader.OpenAsync(tar, false, null, CancellationToken.None, Setup.Value))
            reader.Should().BeOfType<HelperReader>();
        using (ISandboxedArchiveReader reader = await ISandboxedArchiveReader.OpenAsync(zipInGzip, false, null, CancellationToken.None, Setup.Value))
            reader.Should().BeOfType<TarExeReader>();
    }

    [Fact]
    public void HelperFormats_MapsEachFormat_AndRefusesWhatTheHelperLacks()
    {
        IReadOnlyDictionary<string, int?> all = Setup.Value.Capabilities;
        HelperFormats.ArgumentsFor(ArchiveFormat.Tar, all).Should().Equal("--format", "tar");
        HelperFormats.ArgumentsFor(ArchiveFormat.Rar, all).Should().Equal("--format", "rar,rar5");
        HelperFormats.ArgumentsFor(ArchiveFormat.GZip, all).Should().Equal("--format", "tar,cpio", "--filter", "gzip");
        foreach (ArchiveFormat refused in new[] { ArchiveFormat.Iso, ArchiveFormat.Ar, ArchiveFormat.Udf, ArchiveFormat.Zip, ArchiveFormat.Unknown })
            HelperFormats.ArgumentsFor(refused, all).Should().BeNull();

        var noXz = all.ToDictionary(p => p.Key, p => p.Key == "xz" ? -20 : p.Value);
        HelperFormats.ArgumentsFor(ArchiveFormat.Xz, noXz).Should().BeNull();
        HelperFormats.TakesHeaderCharset(["--format", "7zip"]).Should().BeFalse();
        HelperFormats.TakesHeaderCharset(["--format", "rar,rar5"]).Should().BeTrue();
    }

    // --- Error path: a helper's output that disagrees with itself fails closed ---

    [Fact]
    public void Checks_ASecondListingThatDiffers_ASelectionNotRead_OrAWrongSize_Fail()
    {
        ArchiveHelperOutput first = Listed("E 0 32768 3 - 0 0 0 0061 -", "E 1 32768 2 - 0 0 0 0062 -");
        (long, long, bool)[] both = [(0, 3, false), (1, 2, false)];

        HelperReader.EnsureListingAndReadsAgree(first.Entries, both, Run("E 0 32768 3 - 0 0 0 0061 -", "K 0 3", "E 1 32768 2 - 0 0 0 0062 -", "K 1 2"));

        Action renamed = () => HelperReader.EnsureListingAndReadsAgree(first.Entries, both,
            Run("E 0 32768 3 - 0 0 0 0063 -", "K 0 3", "E 1 32768 2 - 0 0 0 0062 -", "K 1 2"));
        Action oneMissing = () => HelperReader.EnsureListingAndReadsAgree(first.Entries, both,
            Run("E 0 32768 3 - 0 0 0 0061 -", "K 0 3", "E 1 32768 2 - 0 0 0 0062 -"));
        Action shortRead = () => HelperReader.EnsureListingAndReadsAgree(first.Entries, both,
            Run("E 0 32768 3 - 0 0 0 0061 -", "K 0 2", "E 1 32768 2 - 0 0 0 0062 -", "K 1 2"));
        renamed.Should().Throw<ArchiveHelperProtocolException>().WithMessage("*second listing*");
        oneMissing.Should().Throw<ArchiveHelperProtocolException>().WithMessage("*not the selection*");
        shortRead.Should().Throw<ArchiveHelperProtocolException>().WithMessage("*declared size*");
    }

    [Fact]
    public void Checks_OutHoldingAnythingButTheFilesRead_Fails()
    {
        ArchiveHelperOutput output = Run("E 0 32768 1 - 0 0 0 0061 -", "K 0 1", "E 1 32768 1 - 0 0 0 0062 -", "K 1 1");
        string outDir = Directory.CreateDirectory(Path.Combine(_temp.Path, "out")).FullName;
        File.WriteAllText(Path.Combine(outDir, "0"), "a");
        File.WriteAllText(Path.Combine(outDir, "1"), "b");
        HelperReader.EnsureOutDirHoldsExactly(outDir, output);

        File.WriteAllText(Path.Combine(outDir, "2"), "c");
        Action extra = () => HelperReader.EnsureOutDirHoldsExactly(outDir, output);
        extra.Should().Throw<ArchiveHelperProtocolException>();

        File.Delete(Path.Combine(outDir, "2"));
        File.WriteAllText(Path.Combine(outDir, "1"), "bb");
        Action wrongSize = () => HelperReader.EnsureOutDirHoldsExactly(outDir, output);
        wrongSize.Should().Throw<ArchiveHelperProtocolException>();

        File.Delete(Path.Combine(outDir, "1"));
        Action missing = () => HelperReader.EnsureOutDirHoldsExactly(outDir, output);
        missing.Should().Throw<ArchiveHelperProtocolException>();

        Directory.CreateDirectory(Path.Combine(outDir, "1"));
        Action folder = () => HelperReader.EnsureOutDirHoldsExactly(outDir, output);
        folder.Should().Throw<ArchiveHelperProtocolException>();
    }

    // A named stream would move out with its file, unseen by the scan; a hard link would make two
    // destinations share one file's data.
    [Fact]
    public void IsPlainFile_ANamedStreamOrAHardLink_IsFalse()
    {
        string zero = Path.Combine(_temp.Path, "0");
        string one = Path.Combine(_temp.Path, "1");
        File.WriteAllText(zero, "a");
        File.WriteAllText(one, "b");
        QuarantineFiles.IsPlainFile(one).Should().BeTrue();
        File.WriteAllText(one + ":hidden", "payload");
        QuarantineFiles.IsPlainFile(one).Should().BeFalse("a named stream");

        File.Delete(one);
        RunCmd("mklink", "/H", one, zero);
        File.ReadAllText(one).Should().Be("a", "the link was made");
        QuarantineFiles.IsPlainFile(one).Should().BeFalse("a second name");
        QuarantineFiles.IsPlainFile(zero).Should().BeFalse("a second name");
    }

    [Fact]
    public void IsPlainFile_AnEmptyFile_IsTrue()
    {
        string empty = Path.Combine(_temp.Path, "0");
        File.WriteAllBytes(empty, []);

        QuarantineFiles.IsPlainFile(empty).Should().BeTrue();
    }

    // Opened for attributes only: a file another process holds (a scanner) is still plain.
    [Fact]
    public void IsPlainFile_AFileHeldByAnotherReader_IsTrue()
    {
        string zero = Path.Combine(_temp.Path, "0");
        File.WriteAllText(zero, "a");
        using var held = new FileStream(zero, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        QuarantineFiles.IsPlainFile(zero).Should().BeTrue();
    }

    // Asked right before each move, for both readers: a stream planted in the quarantine after the
    // run (here by the test, standing in for a sandbox that broke its rules) fails the whole archive
    // and nothing lands. The planted file is found by its unique content, never by the shared
    // PakkoTarSandbox folder's names, so a parallel run of another project is never touched.
    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Extract_AStreamPlantedBeforeTheMove_FailsTheArchive_NothingLands(string backend)
    {
        string marker = Guid.NewGuid().ToString("N");
        string archive = Tar(("a.txt", "A"), ("b.txt", marker), ("c.txt", "C"));
        string sandboxes = Path.Combine(Path.GetTempPath(), "PakkoTarSandbox");
        bool planted = false;
        var progress = new SyncProgress(_ =>
        {
            if (planted || !Directory.Exists(sandboxes))
                return;
            foreach (string file in Directory.EnumerateFiles(sandboxes, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                try
                {
                    if (new FileInfo(file).Length != marker.Length || !HoldsMarker(file, marker))
                        continue;
                    File.WriteAllText(file + ":planted", "payload");
                    planted = true;
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // another run's file, or one still being written: not the one to plant on
                }
            }
        });
        string dest = Path.Combine(_temp.Path, "dest-planted");

        ArchiveResult result = await Service(backend).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
        }, progress);

        planted.Should().BeTrue("the test planted the stream before b.txt moved");
        result.Errors.Should().ContainSingle().Which.SourcePath.Should().Be(archive);
        (Directory.Exists(dest) ? Directory.GetFiles(dest, "*", SearchOption.AllDirectories) : []).Should().BeEmpty();
    }

    // The plain-file check runs before the mark is copied: a downloaded archive still extracts on both
    // readers, and each file gets the archive's mark.
    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Extract_AMarkedArchive_Lands_WithTheMark(string backend)
    {
        string archive = Tar(("a.txt", "A"), ("d/b.txt", "B"));
        File.WriteAllText(archive + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        (ArchiveResult result, string dest) = await ExtractAsync(backend, archive);

        result.Errors.Should().BeEmpty();
        Tree(dest).Should().Equal(new Dictionary<string, string> { ["a.txt"] = "A", ["d/b.txt"] = "B" });
        foreach (string file in Directory.GetFiles(dest, "*", SearchOption.AllDirectories))
            File.ReadAllText(file + ":Zone.Identifier").Should().Contain("ZoneId=3", file);
    }

    // Shares everything, so another run's file being read here can still be moved or deleted.
    private static bool HoldsMarker(string file, string marker)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd() == marker;
    }

    // T-F479: every backend moves the bar of every operation whose bar a reader drives, for many
    // entries and for one large one. Extract of one large entry is left out: its bar is a poll of
    // the output folder, so it moves only as fast as a clock (a size-poll test would be timed).
    public static TheoryData<string, string, bool> ProgressRows
    {
        get
        {
            var rows = new TheoryData<string, string, bool>();
            foreach (string backend in new[] { TarExe, Helper })
            {
                rows.Add(backend, "Test", false);
                rows.Add(backend, "Test", true);
                rows.Add(backend, "Extract", false);
            }
            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(ProgressRows))]
    public async Task Progress_MovesBetweenTheEnds(string backend, string operation, bool oneLargeEntry)
    {
        string archive = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N")[..8] + ".tar");
        TarBuilder.WriteTar(archive, oneLargeEntry
            ? [new TarBuilder.Entry { Name = "big.bin", Content = new byte[16 << 20], ModifiedUnixSeconds = 1700000000 }]
            : Enumerable.Range(0, 20).Select(i => new TarBuilder.Entry { Name = $"f{i:D2}.bin", Content = new byte[64 << 10], ModifiedUnixSeconds = 1700000000 }));
        var percents = new List<int>();
        var progress = new SyncProgress(r => { lock (percents) percents.Add(r.Percent); });

        ArchiveResult result = operation == "Test"
            ? await Service(backend).TestAsync([archive], progress)
            : await Service(backend).ExtractAsync(new ExtractOptions
            {
                ArchivePaths = [archive],
                DestinationFolder = Path.Combine(_temp.Path, "dest-" + Path.GetRandomFileName()),
                Mode = ExtractMode.SingleFolder,
            }, progress: progress);

        result.Errors.Should().BeEmpty();
        percents.Should().Contain(p => p > 0 && p < 99, "the bar moves between its ends, not only at them");
    }

    [Fact]
    public void ProgressRows_CoverEveryReader()
    {
        string[] readers = [.. typeof(ISandboxedArchiveReader).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ISandboxedArchiveReader).IsAssignableFrom(t))
            .Select(t => t.Name)];

        readers.Should().BeEquivalentTo(["TarExeReader", "HelperReader"],
            "a new reader gets its rows in ProgressRows (T-F479) and its name here");
    }

    private sealed class SyncProgress(Action<ProgressReport> onReport) : IProgress<ProgressReport>
    {
        public void Report(ProgressReport value) => onReport(value);
    }

    [Fact]
    public void IsPlainFile_OnAFileThatIsGone_IsFalse_NotAnException()
        => QuarantineFiles.IsPlainFile(Path.Combine(_temp.Path, "gone")).Should().BeFalse();

    private static void RunCmd(params string[] arguments)
    {
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", .. arguments])
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, string.Join(' ', arguments));
    }

    // --- Both backends: error path ---

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Extract_ACutArchive_IsAnError_NotAThrow(string backend)
    {
        string sound = Tar(("a.txt", new string('x', 5000)), ("b.txt", "B"));
        string cut = Path.Combine(_temp.Path, "cut-" + backend + ".tar");
        File.WriteAllBytes(cut, File.ReadAllBytes(sound)[..2048]);

        (ArchiveResult result, string dest) = await ExtractAsync(backend, cut);

        result.Errors.Should().NotBeEmpty();
        (Directory.Exists(dest) ? Directory.GetFiles(dest, "*", SearchOption.AllDirectories) : []).Should().BeEmpty();
    }

    // The same through case and a trimmed dot: Windows makes one name of "X", "x." and "x".
    [Theory]
    [InlineData("X")]
    [InlineData("x.")]
    public async Task Helper_AFileAndAFolderOfOneNameOnDisk_FailsOnlyTheEntryUnderIt(string fileName)
    {
        string archive = Tar((fileName, "file"), ("x/y.txt", "inside"), ("z.txt", "Z"));

        (ArchiveResult result, string dest) = await ExtractAsync(Helper, archive);

        result.Errors.Should().ContainSingle().Which.Message.Should().NotContain(".pakko-");
        Tree(dest).Should().HaveCount(2).And.ContainKey("z.txt");
    }

    // A file and a folder of one name: they cannot both land; the operation reports it, never throws,
    // and never names Pakko's staging folder. tar.exe's run fails as a whole ("1 of 2"); the helper
    // fails only the entry under the file, as ZIP does, and the rest lands.
    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Extract_AFileAndAFolderOfOneName_IsReported_NotAThrow(string backend)
    {
        string archive = Tar(("x", "file"), ("x/y.txt", "inside"), ("z.txt", "Z"));

        (ArchiveResult result, string dest) = await ExtractAsync(backend, archive);

        ArchiveError error = result.Errors.Should().ContainSingle().Which;
        error.SourcePath.Should().Be(archive);
        error.Message.Should().NotContain(".pakko-");
        if (backend == TarExe)
        {
            (Directory.Exists(dest) ? Tree(dest) : []).Should().BeEmpty("tar.exe's failed run leaves nothing at the destination");
        }
        else
        {
            error.Message.Should().Contain(Path.Combine(dest, "x"));
            Tree(dest).Should().Equal(new Dictionary<string, string> { ["x"] = "file", ["z.txt"] = "Z" });
        }
    }

    // The folder first, then the file of its name: the file fails alone, by its entry name.
    [Fact]
    public async Task Helper_AFolderThenAFileOfItsName_FailsOnlyTheFile()
    {
        string archive = Tar(("x/y.txt", "inside"), ("x", "file"), ("z.txt", "Z"));

        (ArchiveResult result, string dest) = await ExtractAsync(Helper, archive);

        ArchiveError error = result.Errors.Should().ContainSingle().Which;
        error.SourcePath.Should().Be(archive);
        error.Message.Should().NotContain(".pakko-").And.Contain("'x'");
        Tree(dest).Should().Equal(new Dictionary<string, string> { ["x/y.txt"] = "inside", ["z.txt"] = "Z" });
    }

    // --- Through the process's gate (T-F470 3b-ii): the services ask it, and read as it decided ---

    [NativeX64Fact]
    public async Task Gate_ExtractionAndTheScan_ReadThroughTheBackendItChose()
    {
        var gate = new ArchiveintGate(new GroupPolicyOptions(), ArchiveHelperBuild.ExePath);
        (await gate.DecideAsync()).UsesHelper.Should().BeTrue();
        string archive = Tar(("日本語.txt", "EVIL"), ("emoji-\U0001F600.txt", "fine"));
        string dest = Path.Combine(_temp.Path, "dest-gate");

        ArchiveResult result = await new TarSandboxedService(new GroupPolicyOptions(), gate).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
        });
        var scanner = new FakeAmsiScanner { DetectedText = "EVIL" };
        ThreatScanResult scan = await new AntivirusScanService(
            () => Task.FromResult(new TarCapabilities()), new GroupPolicyOptions(), () => scanner, () => AmsiProviderState.Usable, gate)
            .ScanAsync(new AntivirusScanOptions { ArchivePaths = [archive] });

        result.Errors.Should().BeEmpty();
        Tree(dest).Should().BeEquivalentTo(new Dictionary<string, string> { ["日本語.txt"] = "EVIL", ["emoji-\U0001F600.txt"] = "fine" });
        scan.Findings.Should().ContainSingle(f => f.Verdict == ThreatVerdict.ThreatDetected).Which.EntryPath.Should().Be("日本語.txt");
    }

    // The listing and Test take the gate too: names tar.exe prints only in the ANSI page come back exact.
    [NativeX64Fact]
    public async Task Gate_ListingAndTest_ReadThroughTheBackendItChose()
    {
        var gate = new ArchiveintGate(new GroupPolicyOptions(), ArchiveHelperBuild.ExePath);
        string[] names = ["日本語.txt", "emoji-\U0001F600.txt"];
        string archive = Tar([.. names.Select(n => (n, (string?)n))]);
        var service = new TarSandboxedService(new GroupPolicyOptions(), gate);
        var tested = new List<string>();

        ArchiveListResult list = await service.ListEntriesAsync(archive);
        ArchiveResult test = await service.TestAsync([archive], entryFilter: (name, isFolder) => { tested.Add(name); return true; });

        list.Entries.Select(e => e.Path).Should().BeEquivalentTo(names);
        test.Errors.Should().BeEmpty();
        tested.Should().BeEquivalentTo(names, "the filter is asked about the entries as the helper names them");
    }

    [Fact]
    public async Task Gate_ThatChoseTarExe_ReadsWithTarExe()
    {
        var gate = new ArchiveintGate(new GroupPolicyOptions(), Path.Combine(_temp.Path, "no-helper.exe"));
        string archive = Tar(("a.txt", "A"));
        string dest = Path.Combine(_temp.Path, "dest-tar");

        ArchiveResult result = await new TarSandboxedService(new GroupPolicyOptions(), gate).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
        });

        (await gate.DecideAsync()).Cause.Should().Contain("no archive helper");
        result.Errors.Should().BeEmpty();
        Tree(dest).Should().BeEquivalentTo(new Dictionary<string, string> { ["a.txt"] = "A" });
    }

    // --- Both backends: what the sweep under the helper found (3b-ii) ---

    // A 7z's CRC mismatch is libarchive's ARCHIVE_WARN on the entry's data: a failure, never a pass.
    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Test_ASevenZipWithAFlippedDataByte_Fails(string backend)
    {
        if (backend == TarExe && !await TarReadsSevenZipAsync())
            return;
        string archive = Path.Combine(_temp.Path, "damaged-" + backend + ".7z");
        byte[] bytes = File.ReadAllBytes(Fixture("valid.7z"));
        bytes[40] ^= 0x55;
        File.WriteAllBytes(archive, bytes);

        ArchiveResult result = await Service(backend).TestAsync([archive]);

        result.Errors.Should().ContainSingle().Which.SourcePath.Should().Be(archive);
    }

    [Theory]
    [InlineData(TarExe, "encrypted.7z")]
    [InlineData(Helper, "encrypted.7z")]
    [InlineData(TarExe, "encrypted_headers.7z")]
    [InlineData(Helper, "encrypted_headers.7z")]
    public async Task Extract_AnEncryptedSevenZip_SaysPasswordProtected(string backend, string fixture)
    {
        if (backend == TarExe && !await TarReadsSevenZipAsync())
            return;
        (ArchiveResult result, _) = await ExtractAsync(backend, Fixture(fixture));

        result.Errors.Should().ContainSingle().Which.Message.Should().Be(
            "This archive is password-protected. Passwords are supported for ZIP archives only.");
    }

    // --- Harness ---

    // SkipIfFormatUnsupported("7z")'s check, for the tar.exe leg of a theory.
    private static async Task<bool> TarReadsSevenZipAsync() =>
        (await new TarSandboxedService(new GroupPolicyOptions()).DetectCapabilitiesAsync()).Supports7z;

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name);

    private static bool RoundTripsThroughAnsi(string name)
    {
        Encoding ansi = new Archiver.Core.Text.TextBoundary((int)TarCodePage.Ansi).Strict;
        try
        {
            return ansi.GetString(ansi.GetBytes(name)) == name;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static ArchiveHelperOutput Listed(params string[] entries) => Fed(ArchiveHelperMode.List, entries, 0);

    private static ArchiveHelperOutput Run(params string[] lines) => Fed(ArchiveHelperMode.Extract, lines, 0);

    private static ArchiveHelperOutput Fed(ArchiveHelperMode mode, string[] lines, int exit)
    {
        var output = new ArchiveHelperOutput(mode);
        foreach (string line in lines.Prepend("V 6C").Append("D"))
            output.Feed(Encoding.ASCII.GetBytes(line));
        output.Finish(exit);
        return output;
    }

    private static HelperSetup Probe()
    {
        string archive = Path.Combine(Path.GetTempPath(), "pakko-probe-" + Guid.NewGuid().ToString("N") + ".tar");
        TarBuilder.WriteTar(archive, [new TarBuilder.Entry { Name = "p", Content = "p"u8.ToArray() }]);
        try
        {
            using TarSandboxScope scope = TarSandboxScope.CreateAsync(archive, false, CancellationToken.None, helperSource: ArchiveHelperBuild.ExePath)
                .GetAwaiter().GetResult();
            var output = new ArchiveHelperOutput(ArchiveHelperMode.Probe);
            scope.RunHelperAsync(ArchiveHelperMode.Probe, [], null, output, CancellationToken.None).GetAwaiter().GetResult();
            return new HelperSetup(ArchiveHelperBuild.ExePath, output.Capabilities);
        }
        finally
        {
            File.Delete(archive);
        }
    }

    private static TarSandboxedService Service(string backend) =>
        new(new GroupPolicyOptions()) { Helper = backend == Helper ? Setup.Value : null };

    private static AntivirusScanService Antivirus(string backend, FakeAmsiScanner scanner) =>
        new(new TarCapabilities(), groupPolicyOptions: new GroupPolicyOptions(), () => scanner, providerState: () => AmsiProviderState.Usable)
        {
            Helper = backend == Helper ? Setup.Value : null,
        };

    private async Task<(ArchiveResult Result, string Dest)> ExtractAsync(
        string backend, string archive, IReadOnlyList<string>? selected = null, Func<string, bool, bool>? filter = null,
        ConflictBehavior onConflict = ConflictBehavior.Rename)
    {
        string dest = Path.Combine(_temp.Path, "dest-" + Path.GetRandomFileName());
        ArchiveResult result = await Service(backend).ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            OnConflict = onConflict,
            SelectedEntryPaths = selected,
            EntryFilter = filter,
        });
        return (result, dest);
    }

    private static Dictionary<string, string> Tree(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText);

    // A pax tar, each name stored as UTF-8 in a "path" record; a null content is a folder.
    private string Tar(params (string Name, string? Content)[] entries)
    {
        string path = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N")[..8] + ".tar");
        TarBuilder.WriteTar(path, entries.SelectMany(e => new[]
        {
            TarBuilder.PaxPath(e.Name),
            e.Content is null
                ? new TarBuilder.Entry { Name = "x/", TypeFlag = '5', ModifiedUnixSeconds = 1700000000 }
                : new TarBuilder.Entry { Name = "x", Content = Encoding.UTF8.GetBytes(e.Content), ModifiedUnixSeconds = 1700000000 },
        }));
        return path;
    }

    private static string Gzip(string file)
    {
        string gz = file + ".gz";
        using (FileStream source = File.OpenRead(file))
        using (FileStream target = File.Create(gz))
        using (var gzip = new System.IO.Compression.GZipStream(target, System.IO.Compression.CompressionLevel.Fastest))
            source.CopyTo(gzip);
        return gz;
    }
}
