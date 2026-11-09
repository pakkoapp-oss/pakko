using System.Text;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F470: the archive helper's protocol, on the real helper and the real archiveint.dll, launched
/// as the product will: a copy in bin\ of a quarantine (bin and list read-execute, out modify) in
/// an AppContainer, with the archive as stdin. Measured behaviour: docs/DECISIONS.md, "T-F470".
/// </summary>
[Collection("TarSandbox")]
public sealed class ArchiveHelperProtocolTests : IDisposable
{
    private const long Mtime = 1700000000;
    private static readonly byte[] Hello = "hello\n"u8.ToArray();

    private readonly TempDirectory _temp = new();
    private readonly AppContainerProfile _profile = new("Pakko.TarSandbox.Test." + Guid.NewGuid());

    public void Dispose()
    {
        _temp.Dispose();
        try { _profile.Delete(); } catch { /* best-effort: a leftover test profile is harmless */ }
    }

    // --- Happy path ---

    [Fact]
    public async Task Probe_ReportsTheVersionAndEverySupportCall()
    {
        Run run = await RunAsync(null, ["probe"]);

        run.ExitCode.Should().Be(0);
        run.Lines[0].Should().StartWith("V ");
        Encoding.ASCII.GetString(Convert.FromHexString(run.Lines[0][2..])).Should().StartWith("libarchive 3.");
        run.Lines.Where(l => l.StartsWith("C ", StringComparison.Ordinal)).Select(l => l.Split(' ')[1])
            .Should().Equal("tar", "cpio", "7zip", "rar", "rar5", "cab", "gzip", "bzip2", "compress", "xz", "lzma", "lzip", "zstd");
        run.Lines[^1].Should().Be("D");
    }

    [Fact]
    public async Task List_PaxNamesOutsideAnyAnsiPage_ComeBackExact()
    {
        string[] names = ["файл.txt", "日本語.txt", "\U0001F600.txt", "a€b.txt", "plain.txt"];
        string archive = Tar(names.SelectMany(n => new[] { TarBuilder.PaxPath(n), File("x") }));

        Run run = await RunAsync(archive, ["list", "--format", "tar"]);

        run.ExitCode.Should().Be(0);
        run.Entries.Select(e => e.Name).Should().Equal(names);
        run.Lines[^1].Should().Be("D");
    }

    [Fact]
    public async Task List_RawUstarName_IsReadInTheGivenPage_AndStrictUtf8RefusesIt()
    {
        byte[] raw = TarCodePage.Encoding(1251).GetBytes("ЖуК.txt");
        string archive = Tar([new TarBuilder.Entry { Name = "x", NameBytes = raw, Content = Hello, ModifiedUnixSeconds = Mtime }]);

        Run cp1251 = await RunAsync(archive, ["list", "--format", "tar", "--hdrcharset", "CP1251"]);
        Run utf8 = await RunAsync(archive, ["list", "--format", "tar", "--hdrcharset", "UTF-8"]);

        cp1251.Entries.Single().Name.Should().Be("ЖуК.txt");
        utf8.ExitCode.Should().Be(0);
        utf8.Lines.Should().Contain(l => l.StartsWith("W 0 ", StringComparison.Ordinal));
        utf8.Entries.Single().Name.Should().BeNull("a name that is not UTF-8 is NULL under hdrcharset=UTF-8");
    }

    [Fact]
    public async Task Extract_WritesOnlyTheSelectedFiles_UnderTheirIndex_WithTheirTime()
    {
        string archive = Tar([File("a.txt", "A"), File("b.txt", "B"), File("c.txt", "C")]);

        Run run = await RunAsync(archive, ["extract", "--format", "tar", "--select", @"list\sel.txt", "--max-total", "1000"], "0\n2\n");

        run.ExitCode.Should().Be(0);
        run.Lines.Should().Contain(["K 0 1", "K 2 1"]);
        run.OutFiles.Should().BeEquivalentTo(["0", "2"]);
        System.IO.File.ReadAllText(run.Out("0")).Should().Be("A");
        System.IO.File.ReadAllText(run.Out("2")).Should().Be("C");
        System.IO.File.GetLastWriteTimeUtc(run.Out("0")).Should().Be(DateTimeOffset.FromUnixTimeSeconds(Mtime).UtcDateTime);
    }

    [Fact]
    public async Task Extract_SparseEntry_LandsWithItsHoles()
    {
        string archive = Path.Combine(_temp.Path, "sparse.tar");
        System.IO.File.WriteAllBytes(archive, GnuSparseTar());

        Run run = await RunAsync(archive, ["extract", "--format", "tar", "--select", @"list\sel.txt", "--max-total", "1000000"], "0\n");

        run.ExitCode.Should().Be(0);
        byte[] expected = new byte[3 * 65536];
        Array.Fill(expected, (byte)'A', 0, 512);
        Array.Fill(expected, (byte)'B', 131072, 512);
        byte[] actual = System.IO.File.ReadAllBytes(run.Out("0"));
        actual.Length.Should().Be(expected.Length);
        Enumerable.Range(0, actual.Length).FirstOrDefault(i => actual[i] != expected[i], -1).Should().Be(-1, "the first differing offset");
    }

    // A hole costs the disk what data does: NTFS zero-fills up to the end the archive claims.
    [Theory]
    [InlineData(4096L, 8L << 30)]
    [InlineData(4L << 30, (4L << 30) + 512)]
    public async Task Extract_ASparseEntryClaimingMoreThanTheTotal_Stops(long secondOffset, long realSize)
    {
        string archive = Path.Combine(_temp.Path, "bomb.tar");
        System.IO.File.WriteAllBytes(archive, GnuSparseTar(secondOffset, realSize));

        Run run = await RunAsync(archive, ["extract", "--format", "tar", "--select", @"list\sel.txt", "--max-total", "1000000"], "0\n");

        run.ExitCode.Should().Be(1);
        run.Lines.Should().Contain("F -3 .");
        new FileInfo(run.Out("0")).Length.Should().BeLessThanOrEqualTo(1000000);
    }

    [Fact]
    public async Task Test_ReadsTheSelectionWithoutWriting()
    {
        string archive = Tar([File("a.txt", "A"), File("b.txt", "B")]);

        Run run = await RunAsync(archive, ["test", "--format", "tar", "--select", @"list\sel.txt"], "0\n1\n");

        run.ExitCode.Should().Be(0);
        run.Lines.Should().Contain(["K 0 1", "K 1 1", "D"]);
        run.OutFiles.Should().BeEmpty();
    }

    // T-F479: Test reports a large entry's progress as it reads it; block boundaries are libarchive's
    // own (they may differ between 3.8.x and 3.5.2), so only the properties are pinned.
    [Fact]
    public async Task Test_ReportsALargeEntrysProgress_BeforeItsRead()
    {
        const int size = (3 << 20) + 1;
        string archive = Tar([new TarBuilder.Entry { Name = "big.bin", Content = new byte[size], ModifiedUnixSeconds = Mtime }]);

        Run run = await RunAsync(archive, ["test", "--format", "tar", "--select", @"list\sel.txt"], "0\n");

        run.ExitCode.Should().Be(0);
        long[] progress = [.. run.Lines.Where(l => l.StartsWith("P 0 ", StringComparison.Ordinal)).Select(l => long.Parse(l[4..], System.Globalization.CultureInfo.InvariantCulture))];
        progress.Should().HaveCountGreaterThanOrEqualTo(2);
        progress.Zip(progress.Skip(1)).Should().OnlyContain(p => p.Second - p.First >= 1 << 20, "a step is at least 1 MiB");
        progress[0].Should().BeGreaterThanOrEqualTo(1 << 20);
        progress[^1].Should().BeLessThanOrEqualTo(size);
        int read = Array.IndexOf(run.Lines, $"K 0 {size}");
        read.Should().BeGreaterThan(Array.LastIndexOf(run.Lines, $"P 0 {progress[^1]}"), "progress comes before the read");
    }

    [Fact]
    public async Task ProgressLines_AreTestsAlone_AndNotForASmallEntry()
    {
        string big = Tar([new TarBuilder.Entry { Name = "big.bin", Content = new byte[(2 << 20) + 1], ModifiedUnixSeconds = Mtime }]);
        string small = Tar([new TarBuilder.Entry { Name = "s.bin", Content = new byte[(1 << 20) - 1], ModifiedUnixSeconds = Mtime }]);

        Run extract = await RunAsync(big, ["extract", "--format", "tar", "--select", @"list\sel.txt", "--max-total", "10000000"], "0\n");
        Run test = await RunAsync(small, ["test", "--format", "tar", "--select", @"list\sel.txt"], "0\n");

        extract.ExitCode.Should().Be(0);
        extract.Lines.Should().NotContain(l => l.StartsWith("P ", StringComparison.Ordinal));
        test.Lines.Should().Contain($"K 0 {(1 << 20) - 1}").And.NotContain(l => l.StartsWith("P ", StringComparison.Ordinal));
    }

    // T-F479: a Test's lines reach Pakko as each is written, not when the run ends. Made, not timed:
    // the archive comes through a pipe that holds back everything after the first entry, so the
    // helper waits on stdin; its "K 0" either arrives now or (buffered) only after the rest is sent.
    // Held back: after the first entry and the next header (its "K"), or halfway through the first
    // entry (its "P", the one large file of a 7z).
    [Theory]
    [InlineData(512 + (2 << 20) + 512, "K 0 2097152")]
    [InlineData(512 + (3 << 19), "P 0 ")]
    public async Task Test_ALineArrives_WhileTheHelperStillReads(int firstPart, string awaited)
    {
        const int size = 2 << 20;
        string whole = Tar([new TarBuilder.Entry { Name = "a.bin", Content = new byte[size], ModifiedUnixSeconds = Mtime }, File("b.txt", "B")]);
        byte[] bytes = System.IO.File.ReadAllBytes(whole);

        using var stdin = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out, HandleInheritability.None);
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lines = new List<string>();
        Task<Run> running = RunAsync(null, ["test", "--format", "tar", "--select", @"list\sel.txt"], "0\n1\n",
            new Microsoft.Win32.SafeHandles.SafeFileHandle(stdin.ClientSafePipeHandle.DangerousGetHandle(), ownsHandle: false), // NOSONAR: S3869 — the pipe outlives the run
            line =>
            {
                string text = Encoding.ASCII.GetString(line);
                lock (lines)
                    lines.Add(text);
                if (text.StartsWith(awaited, StringComparison.Ordinal))
                    firstRead.TrySetResult();
            },
            stdin.DisposeLocalCopyOfClientHandle);
        // The write blocks until the helper reads; a launch that failed would leave it blocked, so
        // the launch's own failure is awaited first (review of T-F479).
        Task sent = Task.Run(async () =>
        {
            await stdin.WriteAsync(bytes.AsMemory(0, firstPart));
            await stdin.FlushAsync();
        });
        if (await Task.WhenAny(sent, running) == running)
            await running;
        await sent;

        var arrived = await Task.WhenAny(firstRead.Task, running, Task.Delay(TimeSpan.FromSeconds(60)));
        bool sawItEarly = arrived == firstRead.Task;
        await stdin.WriteAsync(bytes.AsMemory(firstPart));
        stdin.Dispose();
        Run run = await running;

        sawItEarly.Should().BeTrue("the helper writes each read line through, while it still waits for the rest");
        run.ExitCode.Should().Be(0);
        lines.Should().Contain(["K 0 " + size, "K 1 1", "D"]);
    }

    // --- Security & boundary ---

    [Fact]
    public async Task List_Links_CarryTheirTarget_AndAHardLinkHasNoFileType()
    {
        string archive = Tar([File("f.txt"), new TarBuilder.Entry { Name = "sym", TypeFlag = '2', LinkName = "f.txt" },
            new TarBuilder.Entry { Name = "hard", TypeFlag = '1', LinkName = "f.txt" }]);

        Run run = await RunAsync(archive, ["list", "--format", "tar"]);

        run.Entries[1].Should().Match<HelperEntry>(e => e.FileType == 0xA000 && e.Link == "f.txt");
        run.Entries[2].Should().Match<HelperEntry>(e => e.FileType == 0 && e.Link == "f.txt");
    }

    [Theory]
    [InlineData("1\n")]
    [InlineData("2\n")]
    public async Task Extract_ALinkInTheSelection_IsRefused(string selection)
    {
        string archive = Tar([File("f.txt"), new TarBuilder.Entry { Name = "sym", TypeFlag = '2', LinkName = "f.txt" },
            new TarBuilder.Entry { Name = "hard", TypeFlag = '1', LinkName = "f.txt" }]);

        Run run = await RunAsync(archive, ["extract", "--format", "tar", "--select", @"list\sel.txt", "--max-total", "1000"], selection);

        run.ExitCode.Should().Be(1);
        run.Lines.Should().Contain("F -5 .");
        run.OutFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_NamesNeverBecomePaths()
    {
        string archive = Tar([File("../up.txt", "U"), File("/abs.txt", "A"), File("C:/drive.txt", "D"), File("a:stream", "S"), File("CON", "C")]);

        Run run = await RunAsync(archive, ["extract", "--format", "tar", "--select", @"list\sel.txt", "--max-total", "1000"], "0\n1\n2\n3\n4\n");

        run.ExitCode.Should().Be(0);
        run.OutFiles.Should().BeEquivalentTo(["0", "1", "2", "3", "4"]);
        Directory.EnumerateFileSystemEntries(run.Root).Select(Path.GetFileName).Should().BeEquivalentTo(["bin", "list", "out"]);
        Directory.EnumerateFileSystemEntries(_temp.Path).Select(Path.GetFileName).Should().NotContain(["up.txt", "abs.txt"]);
    }

    [Fact]
    public async Task Extract_TheTotalRunsAcrossEntries()
    {
        string archive = Tar([File("a.txt", "abc"), File("b.txt", "def")]);

        Run run = await RunAsync(archive, ["extract", "--format", "tar", "--select", @"list\sel.txt", "--max-total", "5"], "0\n1\n");

        run.ExitCode.Should().Be(1);
        run.Lines.Should().Contain(["K 0 3", "F -3 ."]);
        run.Lines.Should().NotContain(l => l.StartsWith("K 1 ", StringComparison.Ordinal));
    }

    // A time FILETIME cannot hold (before 1601, or past year 30828) is not a write failure: the
    // file lands, without its time.
    [Theory]
    [InlineData("-20000000000")]
    [InlineData("4611686018427387904")]
    public async Task Extract_AnMtimeOutsideFileTime_StillLands(string mtime)
    {
        string archive = Tar([Pax("mtime", mtime), File("a.txt", "A")]);

        Run run = await RunAsync(archive, ["extract", "--format", "tar", "--select", @"list\sel.txt", "--max-total", "100"], "0\n");

        run.ExitCode.Should().Be(0);
        run.Lines.Should().Contain(["K 0 1", "D"]);
        System.IO.File.ReadAllText(run.Out("0")).Should().Be("A");
        System.IO.File.GetLastWriteTimeUtc(run.Out("0")).Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromHours(1), "an unrepresentable time is not set, never wrapped");
    }

    [Fact]
    public async Task List_ACharsetLibarchiveRefuses_FailsWithItsCode()
    {
        string archive = Tar([File("a.txt")]);

        Run run = await RunAsync(archive, ["list", "--format", "tar", "--hdrcharset", "NOPE-XX"]);

        run.ExitCode.Should().Be(1);
        run.Lines.Should().Contain(l => l.StartsWith("F -4 ", StringComparison.Ordinal));
        run.Lines.Should().NotContain(l => l.StartsWith("E ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Extract_PastTheTotal_Stops()
    {
        string archive = Tar([File("a.txt", "0123456789")]);

        Run run = await RunAsync(archive, ["extract", "--format", "tar", "--select", @"list\sel.txt", "--max-total", "5"], "0\n");

        run.ExitCode.Should().Be(1);
        run.Lines.Should().Contain("F -3 .");
    }

    [Fact]
    public async Task List_AFormatPakkoDidNotName_IsNotRead()
    {
        string archive = Path.Combine(_temp.Path, "a.cpio");
        System.IO.File.WriteAllBytes(archive, NewcCpio("a.txt", Hello));

        Run asTar = await RunAsync(archive, ["list", "--format", "tar"]);
        Run asCpio = await RunAsync(archive, ["list", "--format", "cpio"]);

        asTar.ExitCode.Should().Be(1);
        asTar.Lines.Should().Contain(l => l.StartsWith("F ", StringComparison.Ordinal));
        asCpio.Entries.Single().Name.Should().Be("a.txt");
    }

    // --- Misuse ---

    // One run's arguments per row, split on '|'; "" is no arguments at all.
    private static readonly string[] BadArgumentRows =
    [
        "",
        @"dump",
        @"probe|--format|tar",
        @"list",
        @"list|--format|",
        @"list|--format|tar,tar",
        @"list|--format|tar,",
        @"list|--format|mtree",
        @"list|--format|tar|--format|cpio",
        @"list|--format|tar|--hdrcharset|cp1251;x",
        @"list|--format|tar|--hdrcharset",
        @"list|--format|tar|--select|list\sel.txt",
        @"list|--format|gzip",
        @"list|--format|tar|--filter|cpio",
        @"list|--format|tar|--filter|",
        @"list|--format|tar|--filter|gzip|--filter|xz",
        @"extract|--format|tar|--select|list\sel.txt",
        @"extract|--format|tar|--max-total|10",
        @"extract|--format|tar|--select|list\sel.txt|--max-total|1e3",
    ];

    public static TheoryData<string[]> BadArguments => [.. BadArgumentRows.Select(r => r.Length == 0 ? [] : r.Split('|'))];

    [Theory]
    [MemberData(nameof(BadArguments))]
    public async Task BadArguments_AreAUsageError(string[] arguments)
    {
        string archive = Tar([File("a.txt")]);

        Run run = await RunAsync(archive, arguments, "0\n");

        run.ExitCode.Should().Be(2);
        run.OutFiles.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("00\n")]
    [InlineData("01\n")]
    [InlineData("a\n")]
    [InlineData(" 0\n")]
    [InlineData("0\r\n")]
    [InlineData("1\n0\n")]
    [InlineData("0\n0\n")]
    [InlineData("\n")]
    public async Task AMalformedSelection_IsRefused(string selection)
    {
        string archive = Tar([File("a.txt"), File("b.txt")]);

        Run run = await RunAsync(archive, ["extract", "--format", "tar", "--select", @"list\sel.txt", "--max-total", "100"], selection);

        run.ExitCode.Should().Be(5);
        run.OutFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task ASelectionPastTheLastEntry_IsRefused()
    {
        string archive = Tar([File("a.txt")]);

        Run run = await RunAsync(archive, ["extract", "--format", "tar", "--select", @"list\sel.txt", "--max-total", "100"], "0\n7\n");

        run.ExitCode.Should().Be(5);
        run.Lines.Should().Contain("F -6 .");
    }

    // --- Error path ---

    [Fact]
    public async Task ATruncatedArchive_Fails()
    {
        string whole = Tar([File("a.txt", new string('x', 4000))]);
        string cut = Path.Combine(_temp.Path, "cut.tar");
        System.IO.File.WriteAllBytes(cut, System.IO.File.ReadAllBytes(whole)[..1500]);

        Run run = await RunAsync(cut, ["extract", "--format", "tar", "--select", @"list\sel.txt", "--max-total", "100000"], "0\n");

        run.ExitCode.Should().Be(1);
        run.Lines.Should().Contain(l => l.StartsWith("F ", StringComparison.Ordinal));
        run.Lines.Should().NotContain("D");
    }

    // --- Harness ---

    private static TarBuilder.Entry File(string name, string content = "hello\n") =>
        new() { Name = name, Content = Encoding.ASCII.GetBytes(content), ModifiedUnixSeconds = Mtime };

    // A pax extended header ('x') with one "key=value" record for the entry that follows it.
    private static TarBuilder.Entry Pax(string key, string value)
    {
        byte[] body = Encoding.UTF8.GetBytes(" " + key + "=" + value + "\n");
        int length = body.Length;
        while (length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length + body.Length != length)
            length = length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length + body.Length;
        return new TarBuilder.Entry
        {
            Name = "PaxHeader", TypeFlag = 'x',
            Content = [.. Encoding.ASCII.GetBytes(length.ToString(System.Globalization.CultureInfo.InvariantCulture)), .. body],
        };
    }

    private string Tar(IEnumerable<TarBuilder.Entry> entries)
    {
        string path = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N")[..8] + ".tar");
        TarBuilder.WriteTar(path, entries);
        return path;
    }

    private async Task<Run> RunAsync(string? archive, string[] arguments, string? selection = null,
        Microsoft.Win32.SafeHandles.SafeFileHandle? stdinPipe = null, StdOutLineHandler? onLine = null, Action? onStarted = null)
    {
        _profile.EnsureExists();
        using SafeSidHandle sid = _profile.GetSid();
        string root = Path.Combine(_temp.Path, "q-" + Guid.NewGuid().ToString("N")[..8]);
        foreach (string sub in new[] { "bin", "list", "out" })
            Directory.CreateDirectory(Path.Combine(root, sub));
        QuarantineAcl.GrantTraverseOnly(_temp.Path, sid);
        QuarantineAcl.GrantTraverseListReadAttributes(root, sid);
        QuarantineAcl.GrantReadExecute(Path.Combine(root, "bin"), sid);
        QuarantineAcl.GrantReadExecute(Path.Combine(root, "list"), sid);
        QuarantineAcl.GrantModify(Path.Combine(root, "out"), sid);
        string helper = Path.Combine(root, "bin", "Archiver.ArchiveHelper.exe");
        System.IO.File.Copy(ArchiveHelperBuild.ExePath, helper);
        if (selection is not null)
            System.IO.File.WriteAllBytes(Path.Combine(root, "list", "sel.txt"), Encoding.ASCII.GetBytes(selection));

        using var job = SandboxJobObject.Create(1L << 30, TimeSpan.FromMinutes(1));
        using FileStream? stdin = archive is null ? null : new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (onLine is not null)
        {
            (int lineExit, _, _) = await SandboxedProcessLauncher.RunAsync(helper, arguments,
                new ProcessLaunchOptions(AppContainerSid: sid, Job: job.Handle, StdIn: stdinPipe, WorkingDirectory: root,
                    OutputEncoding: Encoding.Latin1, OnProcessStarted: _ => onStarted?.Invoke(),
                    StdOutLines: new StdOutLines(onLine, ArchiveHelperOutput.MaxLineBytes, 1L << 30)),
                CancellationToken.None);
            return new Run(lineExit, [], root);
        }
        byte[] stdout = [];
        (int exitCode, _, _) = await SandboxedProcessLauncher.RunAsync(helper, arguments,
            new ProcessLaunchOptions(AppContainerSid: sid, Job: job.Handle, StdIn: stdin?.SafeFileHandle, WorkingDirectory: root,
                OutputEncoding: Encoding.Latin1, OnStdOutBytes: b => stdout = b),
            CancellationToken.None);
        stdout.All(b => b == (byte)'\n' || (b >= 0x20 && b < 0x7F)).Should().BeTrue("the helper's output is ASCII only");
        return new Run(exitCode, Encoding.ASCII.GetString(stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries), root);
    }

    private sealed record HelperEntry(long Index, uint FileType, string? Name, string? Link);

    private sealed record Run(int ExitCode, string[] Lines, string Root)
    {
        public string Out(string name) => Path.Combine(Root, "out", name);

        public IReadOnlyList<string> OutFiles =>
            [.. Directory.EnumerateFileSystemEntries(Path.Combine(Root, "out")).Select(p => Path.GetFileName(p)!)];

        public IReadOnlyList<HelperEntry> Entries =>
            [.. Lines.Where(l => l.StartsWith("E ", StringComparison.Ordinal)).Select(l => l.Split(' '))
                .Select(p => new HelperEntry(long.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
                    uint.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), Hex16(p[8]), Hex16(p[9])))];

        private static string? Hex16(string field) => field switch
        {
            "-" => null,
            "." => string.Empty,
            _ => new string([.. Enumerable.Range(0, field.Length / 4).Select(i => (char)Convert.ToUInt16(field.Substring(i * 4, 4), 16))]),
        };
    }

    // A GNU old-style sparse entry: 'A' x 512 at 0 and 'B' x 512 at secondOffset, realSize long
    // (by default 192 KiB with the second block at 128 KiB).
    private static byte[] GnuSparseTar(long secondOffset = 131072, long realSize = 196608)
    {
        static string Octal(long v) => Convert.ToString(v, 8).PadLeft(11, '0') + "\0";
        byte[] header = new byte[512];
        void Field(int offset, string value) => Encoding.ASCII.GetBytes(value).CopyTo(header, offset);
        Field(0, "sparse.bin");
        Field(100, "0000644\0");
        Field(108, "0000000\0");
        Field(116, "0000000\0");
        Field(124, "00000002000\0");
        Field(136, Convert.ToString(Mtime, 8).PadLeft(11, '0') + "\0");
        header[156] = (byte)'S';
        Field(257, "ustar  \0");
        Field(386, "00000000000\0" + "00000001000\0");
        Field(410, Octal(secondOffset) + "00000001000\0");
        Field(483, Octal(realSize));
        Field(148, "        ");
        int sum = header.Sum(b => b);
        Field(148, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ");
        byte[] data = [.. Enumerable.Repeat((byte)'A', 512), .. Enumerable.Repeat((byte)'B', 512)];
        return [.. header, .. data, .. new byte[1024]];
    }

    private static byte[] NewcCpio(string name, byte[] data)
    {
        static byte[] Entry(int ino, int mode, string name, byte[] data)
        {
            byte[] n = Encoding.ASCII.GetBytes(name + "\0");
            string h = "070701" + string.Concat(new[] { ino, mode, 0, 0, 1, (int)Mtime, data.Length, 0, 0, 0, 0, n.Length, 0 }.Select(v => v.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)));
            var b = new List<byte>(Encoding.ASCII.GetBytes(h));
            b.AddRange(n);
            while (b.Count % 4 != 0)
                b.Add(0);
            b.AddRange(data);
            while (b.Count % 4 != 0)
                b.Add(0);
            return [.. b];
        }
        return [.. Entry(1, 0x81A4, name, data), .. Entry(0, 0, "TRAILER!!!", [])];
    }
}
