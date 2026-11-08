using System.Text;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F470: the helper is treated as compromisable, so its stdout is read against the protocol
// (docs/ARCHITECTURE.md, "Archive helper") and anything else fails the run.
public sealed class ArchiveHelperOutputTests
{
    private const string V = "V 6C696261726368697665";
    private const string Plain = "E 0 32768 5 1700000000 0 0 0 0061 -";

    // --- Happy path ---

    [Fact]
    public void List_ReadsEveryField_AndNamesExactly()
    {
        // "файл" + U+1F600 as a surrogate pair; a symlink to "a"; a name libarchive could not convert.
        ArchiveHelperOutput run = Parse(ArchiveHelperMode.List,
            V,
            "E 0 32768 12 1700000000 500 0 0 044404300439043B" + "D83DDE00 -",
            "E 1 40960 0 - 0 - 0 0073 0061",
            "W 2 42 4E6F74205554462D38",
            "E 2 32768 - -5 0 1 3 - -",
            "D");

        run.Version.Should().Be("libarchive");
        run.Done.Should().BeTrue();
        run.Warned.Should().Equal(2);
        run.Entries.Should().Equal(
            new ArchiveHelperEntry(0, 0x8000, 12, 1700000000, 500, false, 0, "файл\U0001F600", null),
            new ArchiveHelperEntry(1, 0xA000, 0, null, 0, null, 0, "s", "a"),
            new ArchiveHelperEntry(2, 0x8000, null, -5, 0, true, 3, null, null));
        run.Finish(0).Should().Be(ArchiveHelperExit.Done);
    }

    [Fact]
    public void Extract_ReportsEachReadAsItArrives()
    {
        var seen = new List<(long, long)>();
        var run = new ArchiveHelperOutput(ArchiveHelperMode.Extract, (i, b) => seen.Add((i, b)));
        Feed(run, V, Plain, "E 1 32768 7 - 0 0 0 0062 -", "K 0 5", "K 1 7", "D");

        seen.Should().Equal((0L, 5L), (1L, 7L));
        run.Read.Should().Equal((0L, 5L), (1L, 7L));
    }

    [Fact]
    public void Probe_ReadsEverySupportCall_AndTheMissingExports()
    {
        string[] names = ["tar", "cpio", "7zip", "rar", "rar5", "cab", "gzip", "bzip2", "compress", "xz", "lzma", "lzip", "zstd"];
        ArchiveHelperOutput run = Parse(ArchiveHelperMode.Probe,
            [V, .. names.Select((n, i) => i == 7 ? $"C {n} -20" : i == 12 ? $"C {n} -" : $"C {n} 0"), "D"]);

        run.Capabilities["tar"].Should().Be(0);
        run.Capabilities["bzip2"].Should().Be(-20);
        run.Capabilities["zstd"].Should().BeNull();
        run.Finish(0).Should().Be(ArchiveHelperExit.Done);

        ArchiveHelperOutput missing = Parse(ArchiveHelperMode.List, V, "X archive_read_open", "X archive_entry_size");
        missing.MissingExports.Should().Equal("archive_read_open", "archive_entry_size");
        missing.Finish(4).Should().Be(ArchiveHelperExit.Export);
    }

    [Fact]
    public void EmptyNameAndNullVersion_AreRead()
    {
        ArchiveHelperOutput run = Parse(ArchiveHelperMode.List, "V -", "E 0 16384 0 - 0 0 0 . -", "D");

        run.Version.Should().BeNull();
        run.Entries.Single().Name.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, "F -3 .")]
    [InlineData(1, "F -30 5472756E6361746564")]
    [InlineData(5, "F -6 .")]
    [InlineData(7, "F 0 .")]
    public void AFailedRun_AgreesWithItsExitCode(int exitCode, string failure)
    {
        ArchiveHelperOutput run = Parse(ArchiveHelperMode.Extract, V, Plain, failure);

        run.Finish(exitCode).Should().Be((ArchiveHelperExit)exitCode);
        run.Failure.Should().NotBeNull();
        run.Done.Should().BeFalse();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void UsageAndLoadFailures_PrintNothing(int exitCode)
        => new ArchiveHelperOutput(ArchiveHelperMode.List).Finish(exitCode).Should().Be((ArchiveHelperExit)exitCode);

    // --- Security & boundary ---

    public static TheoryData<string> BrokenEntryLines =>
    [
        "E 0 32768 5 1700000000 0 0 0 0061 - x",          // a field too many
        "E 0 32768 5 1700000000 0 0 0 0061",              // a field too few
        "E 1 32768 5 1700000000 0 0 0 0061 -",            // skips index 0
        "E 00 32768 5 1700000000 0 0 0 0061 -",           // leading zero
        "E 0 65536 5 1700000000 0 0 0 0061 -",            // file type past unsigned short
        "E 0 32768 -5 1700000000 0 0 0 0061 -",           // negative size
        "E 0 32768 99999999999999999999 1 0 0 0 0061 -",  // size past long
        "E 0 32768 5 -0 0 0 0 0061 -",                    // minus zero
        "E 0 32768 5 1700000000 1000000000 0 0 0061 -",   // nanoseconds past a second
        "E 0 32768 5 - 7 0 0 0061 -",                     // nanoseconds without a time
        "E 0 32768 5 1700000000 0 2 0 0061 -",            // encrypted neither 0, 1 nor -
        "E 0 32768 5 1700000000 0 0 -1 0061 -",           // negative sparse count
        "E 0 32768 5 1700000000 0 0 0 0061a -",           // name hex not a whole code unit
        "E 0 32768 5 1700000000 0 0 0 006a -",            // lowercase hex
        "E 0 32768 5 1700000000 0 0 0 00G1 -",            // not hex
        "E 0 32768 5 1700000000 0 0 0 +061 -",            // a sign in hex
        "E 0 32768 +5 1700000000 0 0 0 0061 -",           // a plus sign
        "E 0 32768 5 17000000000000000000 0 0 0 0061 -",  // time past long
        "E 0  32768 5 1700000000 0 0 0 0061 -",           // two spaces
    ];

    [Theory]
    [MemberData(nameof(BrokenEntryLines))]
    public void AMalformedEntry_BreaksTheProtocol(string line)
        => Breaks(ArchiveHelperMode.List, V, line);

    [Fact]
    public void ANameLongerThanAnyWindowsPath_BreaksTheProtocol()
    {
        string name = string.Concat(Enumerable.Repeat("0061", ArchiveHelperOutput.MaxNameUnits + 1));
        Breaks(ArchiveHelperMode.List, V, $"E 0 32768 5 - 0 0 0 {name} -");

        string longest = string.Concat(Enumerable.Repeat("0061", ArchiveHelperOutput.MaxNameUnits));
        Parse(ArchiveHelperMode.List, V, $"E 0 32768 5 - 0 0 0 {longest} {longest}").Entries.Single().Name.Should().HaveLength(ArchiveHelperOutput.MaxNameUnits);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x09)]
    [InlineData(0x0D)]
    [InlineData(0x7F)]
    [InlineData(0x80)]
    [InlineData(0xFF)]
    public void AByteOutsidePrintableAscii_BreaksTheProtocol(byte b)
    {
        var run = new ArchiveHelperOutput(ArchiveHelperMode.List);
        run.Feed(Encoding.ASCII.GetBytes(V));
        byte[] line = [.. Encoding.ASCII.GetBytes("E 0 32768 5 - 0 0 0 0061 -"), b];

        FluentActions.Invoking(() => run.Feed(line)).Should().Throw<ArchiveHelperProtocolException>();
    }

    [Fact]
    public void TheIndexBound_IsTheEntryLimit()
        => Breaks(ArchiveHelperMode.Extract, V, Plain, $"K {ArchiveHelperOutput.MaxEntries} 1");

    // --- Misuse (lines out of place) ---

    public static TheoryData<string, string[]> OutOfPlace => new()
    {
        { "List", [Plain] },                                     // no version first
        { "List", [V, V] },                                      // two versions
        { "List", [V, Plain, "X archive_read_open"] },           // a missing export after an entry
        { "List", [V, "X Archive_Read"] },                       // not an export name
        { "List", [V, "C tar 0"] },                              // a probe line outside probe
        { "Probe", [V, Plain] },                                 // an entry in probe
        { "Probe", [V, "C cpio 0"] },                            // support calls out of order
        { "Probe", [V, "C tar 0", "D"] },                        // a probe missing calls
        { "List", [V, Plain, "K 0 5"] },                         // a read in list
        { "Extract", [V, Plain, "K 1 5"] },                      // a read of an entry not listed
        { "Extract", [V, Plain, "E 1 32768 5 - 0 0 0 0062 -", "K 1 5", "K 0 5"] }, // reads out of order
        { "Extract", [V, Plain, "K 0 5", "K 0 5"] },             // a read twice
        { "List", [V, "W 1 42 ."] },                             // a warning not for the next entry
        { "List", [V, "W 0 42 .", "W 0 42 ."] },                 // two warnings for one entry
        { "List", [V, "W 0 42 41"+ "4"] },                       // half a hex byte
        { "List", [V, "D", Plain] },                             // a line after done
        { "List", [V, "F -30 .", "D"] },                         // done after a failure
        { "List", [V, "Q 1"] },                                  // a line the protocol does not have
        { "List", [V, ""] },                                     // an empty line
        { "List", [V, "D "] },                                   // a trailing space
    };

    [Theory]
    [MemberData(nameof(OutOfPlace))]
    public void ALineOutOfPlace_BreaksTheProtocol(string mode, string[] lines)
        => Breaks(Enum.Parse<ArchiveHelperMode>(mode), lines);

    // --- Error path (an exit code that disagrees with the output) ---

    public static TheoryData<int, string[]> Disagreeing => new()
    {
        { 0, [V, Plain] },              // success without "D"
        { 1, [V, Plain, "D"] },         // a failure that printed "D"
        { 1, [V, Plain] },              // a failure without "F"
        { 1, [V, "F 0 ."] },            // an archive failure with the capability code
        { 7, [V, "F -3 ."] },           // a capability failure with another code
        { 5, [V, "F -3 ."] },           // a selection failure with another code
        { 2, [V] },                     // usage after output
        { 6, [V, Plain, "D"] },         // a write failure that printed "D"
        { 8, [V, "D"] },                // no such exit code
        { -1, [V, "D"] },
    };

    [Theory]
    [MemberData(nameof(Disagreeing))]
    public void AnExitCodeAgainstTheOutput_BreaksTheProtocol(int exitCode, string[] lines)
    {
        ArchiveHelperOutput run = Parse(ArchiveHelperMode.Extract, lines);

        FluentActions.Invoking(() => run.Finish(exitCode)).Should().Throw<ArchiveHelperProtocolException>();
    }

    // --- Harness ---

    private static ArchiveHelperOutput Parse(ArchiveHelperMode mode, params string[] lines)
    {
        var run = new ArchiveHelperOutput(mode);
        Feed(run, lines);
        return run;
    }

    private static void Feed(ArchiveHelperOutput run, params string[] lines)
    {
        foreach (string line in lines)
            run.Feed(Encoding.ASCII.GetBytes(line));
    }

    private static void Breaks(ArchiveHelperMode mode, params string[] lines)
        => FluentActions.Invoking(() => Parse(mode, lines)).Should().Throw<ArchiveHelperProtocolException>();
}
