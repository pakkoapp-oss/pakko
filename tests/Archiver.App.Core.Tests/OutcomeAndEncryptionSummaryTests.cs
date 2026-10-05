using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F211 (the result line, the footer's precedence) and T-F199/T-F202 (browse mode's encryption badge).
public sealed class OutcomeAndEncryptionSummaryTests
{
    private const string Dest = @"C:\out";

    [Fact]
    public void Outcome_CleanCompress_NoDetailsAndShowsTheFirstCreated()
    {
        var result = new ArchiveResult { CreatedFiles = [@"C:\out\a.zip", @"C:\out\b.zip"] };

        var line = OutcomeLine.From(result, TimeSpan.FromSeconds(2.6), extract: false, Dest);

        line.Outcome.Should().Be(OperationOutcome.Completed);
        line.CreatedCount.Should().Be(2);
        line.Seconds.Should().Be(3);
        line.HasDetails.Should().BeFalse();
        line.ShowInFolderPath.Should().Be(@"C:\out\a.zip");
        line.TextKey.Should().Be("OutcomeCompressed");
        line.TextArgs.Should().Equal(3, 2);
    }

    [Fact]
    public void Outcome_CleanExtract_SaysExtracted()
    {
        var result = new ArchiveResult { CreatedFiles = [@"C:\out\a"] };

        var line = OutcomeLine.From(result, TimeSpan.FromSeconds(1), extract: true, Dest);

        line.TextKey.Should().Be("OutcomeExtracted");
        line.TextArgs.Should().Equal(1, 1);
    }

    [Fact]
    public void Outcome_SubSecondRun_SaysOneSecond()
    {
        OutcomeLine.From(new ArchiveResult(), TimeSpan.FromMilliseconds(40), extract: false, Dest).Seconds.Should().Be(1);
    }

    [Fact]
    public void Outcome_Problems_CountsErrorsAndSkipsAndOffersDetails()
    {
        var result = new ArchiveResult
        {
            CreatedFiles = [@"C:\out\a.zip"],
            Errors = [CoreMessages.Error("x", MessageCode.SourceNotFound, "x")],
            SkippedFiles = [CoreMessages.Skip("y", MessageCode.SourceNotFound, "y")],
        };

        var line = OutcomeLine.From(result, TimeSpan.FromSeconds(1), extract: false, Dest);

        line.Outcome.Should().Be(OperationOutcome.Failed);
        line.ProblemCount.Should().Be(2);
        line.HasDetails.Should().BeTrue();
        line.TextKey.Should().Be("OutcomeProblems");
        line.TextArgs.Should().Equal(2);
    }

    // T-F280: a warning is something to show, so the line offers Details instead of plain success.
    [Fact]
    public void Outcome_OnlyAWarning_IsAProblemLineWithDetails()
    {
        var result = new ArchiveResult
        {
            CreatedFiles = [@"C:\out\a"],
            Warnings = [CoreMessages.Warning("a.zip", CoreMessages.Text(MessageCode.LocalHeaderMismatch, "1", "b.txt"))],
        };

        var line = OutcomeLine.From(result, TimeSpan.FromSeconds(1), extract: true, Dest);

        line.Outcome.Should().Be(OperationOutcome.CompletedWithWarnings);
        line.ProblemCount.Should().Be(1);
        line.HasDetails.Should().BeTrue();
        line.TextKey.Should().Be("OutcomeProblems");
        line.TextArgs.Should().Equal(1);
        line.ShowInFolderPath.Should().Be(@"C:\out\a", "the extraction is there to be shown");
    }

    [Fact]
    public void Outcome_ErrorSkipAndWarning_CountsAllThree()
    {
        var result = new ArchiveResult
        {
            Errors = [CoreMessages.Error("x", MessageCode.SourceNotFound, "x")],
            SkippedFiles = [CoreMessages.Skip("y", MessageCode.SourceNotFound, "y")],
            Warnings = [CoreMessages.Warning("a.zip", CoreMessages.Text(MessageCode.LocalHeaderMismatch, "1", "b.txt"))],
        };

        OutcomeLine.From(result, TimeSpan.FromSeconds(1), extract: true, Dest).ProblemCount.Should().Be(3);
    }

    [Fact]
    public void Outcome_SomeSkipped_IsAProblemLineNotSuccess()
    {
        var result = new ArchiveResult
        {
            CreatedFiles = [@"C:\out\a"],
            SkippedFiles = [CoreMessages.Skip("y", MessageCode.SourceNotFound, "y")],
        };

        OutcomeLine.From(result, TimeSpan.FromSeconds(1), extract: true, Dest).TextKey.Should().Be("OutcomeProblems");
    }

    // T-F274's lesson: everything skipped must not read like success.
    [Fact]
    public void Outcome_EverythingSkipped_SaysNothingWasDone()
    {
        var result = new ArchiveResult { SkippedFiles = [CoreMessages.Skip("y", MessageCode.SourceNotFound, "y")] };

        var line = OutcomeLine.From(result, TimeSpan.FromSeconds(1), extract: true, Dest);

        line.Outcome.Should().Be(OperationOutcome.NothingDone);
        line.TextKey.Should().Be("OutcomeNothingDone");
        line.TextArgs.Should().Equal(1);
        line.HasDetails.Should().BeTrue();
    }

    [Fact]
    public void Outcome_NothingCreated_NoFolderToShow()
    {
        var line = OutcomeLine.From(new ArchiveResult(), TimeSpan.FromSeconds(1), extract: false, Dest);

        line.ShowInFolderPath.Should().BeNull();
        line.ExplorerArguments.Should().BeNull();
    }

    [Fact]
    public void ShowInFolder_CreatedItem_SelectsIt()
    {
        var result = new ArchiveResult { CreatedFiles = [@"C:\out\Мій архів.zip"] };

        OutcomeLine.From(result, TimeSpan.FromSeconds(1), extract: false, Dest)
            .ExplorerArguments.Should().Be(@"/select,""C:\out\Мій архів.zip""");
    }

    // A flat extraction reports the destination itself: open it instead of selecting it in its parent.
    [Theory]
    [InlineData(@"C:\out")]
    [InlineData(@"C:\OUT\")]
    public void ShowInFolder_ExtractedIntoTheDestinationItself_OpensIt(string created)
    {
        var result = new ArchiveResult { CreatedFiles = [created] };

        OutcomeLine.From(result, TimeSpan.FromSeconds(1), extract: true, Dest)
            .ExplorerArguments.Should().Be($@"""{created}""");
    }

    // A drive root keeps its backslash; explorer.exe "D:\" opens the root (checked on device).
    [Fact]
    public void ShowInFolder_ExtractedIntoADriveRoot_OpensIt()
    {
        var result = new ArchiveResult { CreatedFiles = [@"D:\"] };

        OutcomeLine.From(result, TimeSpan.FromSeconds(1), extract: true, @"D:\")
            .ExplorerArguments.Should().Be(@"""D:\""");
    }

    [Theory]
    [InlineData(true, true, true, 3, true, FooterLineKind.None)]
    [InlineData(false, true, true, 3, true, FooterLineKind.Outcome)]
    [InlineData(false, true, false, 0, true, FooterLineKind.Outcome)]
    [InlineData(false, false, true, 3, false, FooterLineKind.Selection)]
    [InlineData(false, false, true, 0, false, FooterLineKind.None)]
    [InlineData(false, false, false, 0, true, FooterLineKind.Preview)]
    [InlineData(false, false, false, 0, false, FooterLineKind.None)]
    public void FooterLine_Precedence(bool busy, bool hasOutcome, bool browsing, int selected, bool listHasItems, FooterLineKind expected)
    {
        FooterLine.Pick(busy, hasOutcome, browsing, selected, listHasItems).Should().Be(expected);
    }

    private static ArchiveEntryInfo Entry(string path, EntryEncryption? kind, int? ae = null, bool dir = false) =>
        new() { Path = path, Encryption = kind, AesVersion = ae, IsDirectory = dir };

    [Fact]
    public void Encryption_PlainArchive_NoBadge()
    {
        var summary = EncryptionSummary.Of([Entry("a", EntryEncryption.None), Entry("d", null, dir: true)]);

        summary.IsEncrypted.Should().BeFalse();
        summary.Badge.Should().BeNull();
        summary.TotalFiles.Should().Be(1);
    }

    [Fact]
    public void Encryption_TarListing_NoBadge()
    {
        EncryptionSummary.Of([Entry("a", null)]).IsEncrypted.Should().BeFalse();
    }

    [Fact]
    public void Encryption_AllAes256Ae2_CountsFilesOnly()
    {
        var summary = EncryptionSummary.Of(
            [Entry("a", EntryEncryption.Aes256, 2), Entry("b", EntryEncryption.Aes256, 2), Entry("d", null, dir: true)]);

        summary.Should().Be(new EncryptionSummary(2, 2, EntryEncryption.Aes256, HasAe2: true));
    }

    [Fact]
    public void Encryption_MixedMethods_BadgeIsTheWeakest()
    {
        var summary = EncryptionSummary.Of(
            [Entry("a", EntryEncryption.Aes256, 1), Entry("b", EntryEncryption.ZipCrypto), Entry("c", EntryEncryption.None)]);

        summary.Badge.Should().Be(EntryEncryption.ZipCrypto);
        summary.EncryptedFiles.Should().Be(2);
        summary.TotalFiles.Should().Be(3);
        summary.HasAe2.Should().BeFalse();
    }

    [Fact]
    public void Encryption_UnnamedMethodPresent_BadgeIsUnknown()
    {
        EncryptionSummary.Of([Entry("a", EntryEncryption.Aes256, 2), Entry("b", EntryEncryption.Unknown)])
            .Badge.Should().Be(EntryEncryption.Unknown);
    }
}
