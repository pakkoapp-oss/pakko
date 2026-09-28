using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F211 (the result line) and T-F199/T-F202 (browse mode's encryption badge).
public sealed class OutcomeAndEncryptionSummaryTests
{
    [Fact]
    public void Outcome_CleanRun_NoDetailsAndShowsTheFirstCreated()
    {
        var result = new ArchiveResult { CreatedFiles = [@"C:\out\a.zip", @"C:\out\b.zip"] };

        OutcomeLine line = OutcomeLine.From(result, TimeSpan.FromSeconds(2.6));

        line.Outcome.Should().Be(OperationOutcome.Completed);
        line.CreatedCount.Should().Be(2);
        line.Seconds.Should().Be(3);
        line.HasDetails.Should().BeFalse();
        line.ShowInFolderPath.Should().Be(@"C:\out\a.zip");
    }

    [Fact]
    public void Outcome_SubSecondRun_SaysOneSecond()
    {
        OutcomeLine.From(new ArchiveResult(), TimeSpan.FromMilliseconds(40)).Seconds.Should().Be(1);
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

        OutcomeLine line = OutcomeLine.From(result, TimeSpan.FromSeconds(1));

        line.Outcome.Should().Be(OperationOutcome.Failed);
        line.ProblemCount.Should().Be(2);
        line.HasDetails.Should().BeTrue();
    }

    [Fact]
    public void Outcome_NothingCreated_NoFolderToShow()
    {
        OutcomeLine.From(new ArchiveResult(), TimeSpan.FromSeconds(1)).ShowInFolderPath.Should().BeNull();
    }

    private static ArchiveEntryInfo Entry(string path, EntryEncryption? kind, int? ae = null, bool dir = false) =>
        new() { Path = path, Encryption = kind, AesVersion = ae, IsDirectory = dir };

    [Fact]
    public void Encryption_PlainArchive_NoBadge()
    {
        EncryptionSummary summary = EncryptionSummary.Of([Entry("a", EntryEncryption.None), Entry("d", null, dir: true)]);

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
        EncryptionSummary summary = EncryptionSummary.Of(
            [Entry("a", EntryEncryption.Aes256, 2), Entry("b", EntryEncryption.Aes256, 2), Entry("d", null, dir: true)]);

        summary.Should().Be(new EncryptionSummary(2, 2, EntryEncryption.Aes256, HasAe2: true));
    }

    [Fact]
    public void Encryption_MixedMethods_BadgeIsTheWeakest()
    {
        EncryptionSummary summary = EncryptionSummary.Of(
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
