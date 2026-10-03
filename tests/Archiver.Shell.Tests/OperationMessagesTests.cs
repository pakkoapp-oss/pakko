using System.Globalization;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Shell;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F268: the result text each Explorer command shows, built without any dialog — the same
// strings the MessageBoxW calls in Program.cs showed before the refactor.
public sealed class OperationMessagesTests : IDisposable
{
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;

    public OperationMessagesTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalUiCulture;
        CultureInfo.CurrentCulture = _originalCulture;
    }

    // --- ArchiveResult ---

    [Fact]
    public void ForArchiveResult_Success_ReturnsNull()
    {
        OperationMessages.ForArchiveResult("T", new ArchiveResult()).Should().BeNull();
    }

    [Fact]
    public void ForArchiveResult_Errors_ListsFileNameAndMessage()
    {
        var result = new ArchiveResult
        {
            Errors = [new ArchiveError { SourcePath = @"C:\dir\a.zip", Message = "bad CRC" }],
        };

        OperationMessage message = OperationMessages.ForArchiveResult("T", result)!;

        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Should().Be("a.zip: bad CRC");
    }

    [Fact]
    public void ForArchiveResult_TwelveErrors_ShowsTenAndTheRestCount()
    {
        var result = new ArchiveResult
        {
            Errors = [.. Enumerable.Range(1, 12).Select(i => new ArchiveError { SourcePath = $"f{i}.zip", Message = "x" })],
        };

        string[] lines = OperationMessages.ForArchiveResult("T", result)!.Text.Split(Environment.NewLine);

        lines.Should().HaveCount(11);
        lines[9].Should().Be("f10.zip: x");
        lines[10].Should().Be("…and 2 more");
    }

    [Fact]
    public void ForArchiveResult_SkippedOnly_IsAWarningWithTheSkippedHeader()
    {
        var result = new ArchiveResult
        {
            SkippedFiles = [new SkippedFile { Path = "bad.txt", Reason = "ADS entry" }],
        };

        OperationMessage message = OperationMessages.ForArchiveResult("T", result)!;

        message.Severity.Should().Be(MessageSeverity.Warning);
        message.Text.Should().StartWith("Skipped (1):").And.Contain("bad.txt: ADS entry");
    }

    // T-F209: a coded Core reason follows the UI language; before, it was always English.
    [Fact]
    public void ForArchiveResult_UnderUkrainian_RendersCoreReasonInUkrainian()
    {
        CoreText reason = ArchiveFormatPolicy.GetRefusalReason(
            ArchiveFormat.SevenZip, new TarCapabilities(), new GroupPolicyOptions { DisableTarExtraction = true })!;
        var result = new ArchiveResult { SkippedFiles = [new SkippedFile { Path = "a.7z", Reason = reason.English, Text = reason }] };
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");

        string text = OperationMessages.ForArchiveResult("T", result)!.Text;

        text.Should().Contain("a.7z: Видобування за допомогою tar.exe вимкнено груповою політикою.").And.NotContain("disabled");
    }

    // --- Test (T-F216, T-F274) ---

    private static readonly SkippedFile TarSkip = new() { Path = "b.tar", Reason = "no test" };
    private static readonly SourceResult TestedZip = new() { Path = "a.zip", Outcome = SourceOutcome.Completed };

    [Fact]
    public void ForTestResult_EverythingTested_SaysNoErrors() =>
        OperationMessages.ForTestResult("T", new ArchiveResult { Sources = [TestedZip] })
            .Should().Be(OperationMessages.TestPassed("T"));

    [Fact]
    public void ForTestResult_SomeTestedSomeSkipped_ListsSkipsThenNoErrors()
    {
        OperationMessage message = OperationMessages.ForTestResult("T",
            new ArchiveResult { Sources = [TestedZip], SkippedFiles = [TarSkip] })!;

        message.Severity.Should().Be(MessageSeverity.Warning);
        message.Text.Should().StartWith("Skipped (1):").And.EndWith("No errors detected in the archive(s).");
    }

    [Fact]
    public void ForTestResult_NothingTested_ListsSkipsWithoutNoErrors()
    {
        OperationMessage message = OperationMessages.ForTestResult("T", new ArchiveResult { SkippedFiles = [TarSkip] })!;

        message.Severity.Should().Be(MessageSeverity.Warning);
        message.Text.Should().StartWith("Skipped (1):").And.NotContain("No errors detected");
    }

    [Fact]
    public void ForTestResult_Errors_ListsErrorsOnly()
    {
        var result = new ArchiveResult
        {
            Sources = [TestedZip],
            Errors = [new ArchiveError { SourcePath = "a.zip", Message = "bad CRC" }],
        };

        OperationMessages.ForTestResult("T", result).Should().Be(new OperationMessage("T", MessageSeverity.Error, "a.zip: bad CRC"));
    }

    [Fact]
    public void TestPassed_IsInformation()
    {
        OperationMessages.TestPassed("T").Should()
            .Be(new OperationMessage("T", MessageSeverity.Information, "No errors detected in the archive(s)."));
    }

    // --- HashResult ---

    [Fact]
    public void ForHash_Folder_ShowsTheFourSummaryLines()
    {
        var result = new HashResult { Folder = new FolderHashSummary("AAAA", "BBBB", 3, 2048) };

        OperationMessage message = OperationMessages.ForHash("T", result, []);

        message.Severity.Should().Be(MessageSeverity.Information);
        message.Text.Split(Environment.NewLine).Should().Equal(
            "Files: 3", "Size: 2 KB (2,048 B)", "DataSum: AAAA", "NamesSum: BBBB");
    }

    // T-F291: the folder sums leave out what could not be read; the window lists it, as pakko h does,
    // named relative to the folder's parent (the names NamesSum covers), capped like the file list.
    [Fact]
    public void ForHash_FolderWithFailedEntries_ListsThemUnderTheSummary()
    {
        var result = new HashResult
        {
            Folder = new FolderHashSummary("AAAA", "BBBB", 1, 6),
            Entries =
            [
                new HashEntry(@"C:\data\docs\ok.txt", "00", null),
                new HashEntry(@"C:\data\docs\a\x.bin", null, "Access denied"),
                new HashEntry(@"C:\data\docs\b\x.bin", null, "Skipped junction"),
            ],
        };

        OperationMessage message = OperationMessages.ForHash("T", result, [@"C:\data\docs\"]);

        message.Severity.Should().Be(MessageSeverity.Warning);
        message.Text.Split(Environment.NewLine).Should().Equal(
            "Files: 1", "Size: 6 B", "DataSum: AAAA", "NamesSum: BBBB", "",
            @"docs\a\x.bin: Access denied", @"docs\b\x.bin: Skipped junction");
    }

    [Fact]
    public void ForHash_FolderWithTwelveFailedEntries_ShowsTenAndTheRestCount()
    {
        var result = new HashResult
        {
            Folder = new FolderHashSummary("AAAA", "BBBB", 0, 0),
            Entries = [.. Enumerable.Range(1, 12).Select(i => new HashEntry($@"C:\d\f{i}", null, "x"))],
        };

        string[] lines = OperationMessages.ForHash("T", result, [@"C:\d"]).Text.Split(Environment.NewLine);

        lines.Should().HaveCount(4 + 1 + 11);
        lines[14].Should().Be(@"d\f10: x");
        lines[15].Should().Be("…and 2 more");
    }

    // T-F208: "(2,048 bytes)" had no plural form for other languages; the byte unit has none.
    [Fact]
    public void ForHash_FolderUnderOneKilobyte_ShowsTheSizeOnce()
    {
        var result = new HashResult { Folder = new FolderHashSummary("AAAA", "BBBB", 1, 6) };

        OperationMessages.ForHash("T", result, []).Text.Split(Environment.NewLine)[1].Should().Be("Size: 6 B");
    }

    [Fact]
    public void ForHash_EntryWithError_IsAWarning()
    {
        var result = new HashResult { Entries = [new HashEntry(@"C:\a.txt", null, "Access denied")] };

        OperationMessage message = OperationMessages.ForHash("T", result, []);

        message.Severity.Should().Be(MessageSeverity.Warning);
        message.Text.Should().Be("a.txt: Access denied");
    }

    [Fact]
    public void ForHash_TwelveEntries_ShowsTenAndTheRestCount()
    {
        var result = new HashResult { Entries = [.. Enumerable.Range(1, 12).Select(i => new HashEntry($"f{i}", "00", null))] };

        string[] lines = OperationMessages.ForHash("T", result, []).Text.Split(Environment.NewLine);

        lines.Should().HaveCount(11);
        lines[10].Should().Be("…and 2 more");
    }

    // T-F268 polish (a): a 64-73 character hash does not fit the window's width in any font.
    [Fact]
    public void ForHash_IsPreformattedForBothFilesAndFolders()
    {
        OperationMessages.ForHash("T", new HashResult { Entries = [new HashEntry(@"C:.txt", "00", null)] }, []).Preformatted.Should().BeTrue();
        OperationMessages.ForHash("T", new HashResult { Folder = new FolderHashSummary("AAAA", "BBBB", 1, 6) }, []).Preformatted.Should().BeTrue();
    }

    [Fact]
    public void OtherResults_AreNotPreformatted()
    {
        var failed = new ArchiveResult { Errors = [new ArchiveError { SourcePath = "a.zip", Message = "x" }] };
        OperationMessages.ForArchiveResult("T", failed)!.Preformatted.Should().BeFalse();
        OperationMessages.ForScan("T", new ThreatScanResult { OverallVerdict = ThreatVerdict.Clean }, archiveCount: 1).Preformatted.Should().BeFalse();
    }

    // --- ThreatScanResult ---

    [Fact]
    public void ForScan_Clean_IsInformation()
    {
        OperationMessage message = OperationMessages.ForScan("T", new ThreatScanResult { OverallVerdict = ThreatVerdict.Clean }, archiveCount: 1);

        message.Should().Be(new OperationMessage("T", MessageSeverity.Information, "No threats found in this archive."));
    }

    // T-F268 step 6 polish (b): several clean archives are not "this archive".
    [Fact]
    public void ForScan_SeveralCleanArchives_SaysSelectedArchives()
    {
        OperationMessage message = OperationMessages.ForScan("T", new ThreatScanResult { OverallVerdict = ThreatVerdict.Clean }, archiveCount: 3);

        message.Text.Should().Be("No threats found in the selected archives.");
    }

    [Fact]
    public void ForScan_Threat_IsAnErrorNamingTheEntry()
    {
        var result = new ThreatScanResult
        {
            OverallVerdict = ThreatVerdict.ThreatDetected,
            Findings =
            [
                new ThreatFinding { ArchivePath = @"C:\a.zip", EntryPath = "x.exe", Verdict = ThreatVerdict.ThreatDetected },
                new ThreatFinding { ArchivePath = @"C:\a.zip", EntryPath = "ok.txt", Verdict = ThreatVerdict.Clean },
            ],
        };

        OperationMessage message = OperationMessages.ForScan("T", result, archiveCount: 1);

        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Should().Be("a.zip/x.exe: threat detected");
    }

    [Fact]
    public void ForScan_InconclusiveOnly_IsAWarningWithTheReason()
    {
        var result = new ThreatScanResult
        {
            OverallVerdict = ThreatVerdict.Inconclusive,
            Findings = [new ThreatFinding { ArchivePath = "b.zip", Verdict = ThreatVerdict.Inconclusive, Reason = "no AV provider" }],
        };

        OperationMessage message = OperationMessages.ForScan("T", result, archiveCount: 1);

        message.Severity.Should().Be(MessageSeverity.Warning);
        message.Text.Should().Be("b.zip: no AV provider");
    }

    // --- AppLaunchResult ---

    [Fact]
    public void ForLaunch_Launched_ReturnsNull()
    {
        OperationMessages.ForLaunch(AppLaunchResult.Launched).Should().BeNull();
    }

    // T-F235: a rejected stdin path list must not end the command silently.
    [Fact]
    public void ForSelectionNotReceived_IsAnErrorSayingTheOperationFailed()
    {
        OperationMessage message = OperationMessages.ForSelectionNotReceived();

        message.Title.Should().Be("Pakko");
        message.Severity.Should().Be(MessageSeverity.Error);
        message.Text.Should().Be(ResultMessagesLocalizer.Get("ResultOperationFailed"));
    }
}
