using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.Shell.Tests;

public sealed class ShellResultPresenterTests
{
    // --- Classify ---

    [Fact]
    public void Classify_SuccessNoErrorsNoSkips_ReturnsSuccess()
    {
        var result = new ArchiveResult { Success = true };

        ShellResultPresenter.Classify(result).Should().Be(ShellResultOutcome.Success);
    }

    [Fact]
    public void Classify_SuccessFalse_ReturnsFailed()
    {
        var result = new ArchiveResult { Success = false };

        ShellResultPresenter.Classify(result).Should().Be(ShellResultOutcome.Failed);
    }

    [Fact]
    public void Classify_HasErrors_ReturnsFailed()
    {
        var result = new ArchiveResult
        {
            Success = true,
            Errors = [new ArchiveError { SourcePath = "a.txt", Message = "boom" }],
        };

        ShellResultPresenter.Classify(result).Should().Be(ShellResultOutcome.Failed);
    }

    [Fact]
    public void Classify_SuccessWithSkippedFilesOnly_ReturnsSkippedOnly()
    {
        var result = new ArchiveResult
        {
            Success = true,
            SkippedFiles = [new SkippedFile { Path = "bad.txt", Reason = "ADS entry" }],
        };

        ShellResultPresenter.Classify(result).Should().Be(ShellResultOutcome.SkippedOnly);
    }

    [Fact]
    public void Classify_ErrorsAndSkippedFilesBothPresent_ReturnsFailed()
    {
        var result = new ArchiveResult
        {
            Success = true,
            Errors = [new ArchiveError { SourcePath = "a.txt", Message = "boom" }],
            SkippedFiles = [new SkippedFile { Path = "bad.txt", Reason = "ADS entry" }],
        };

        ShellResultPresenter.Classify(result).Should().Be(ShellResultOutcome.Failed);
    }

    // --- BuildSkippedMessage ---

    [Fact]
    public void BuildSkippedMessage_SingleEntry_UsesSingularNoun()
    {
        var skipped = new[] { new SkippedFile { Path = @"C:\dir\bad.txt", Reason = "ADS entry" } };

        var message = ShellResultPresenter.BuildSkippedMessage(skipped);

        message.Should().StartWith("1 entry skipped:");
        message.Should().Contain("bad.txt: ADS entry");
    }

    [Fact]
    public void BuildSkippedMessage_MultipleEntries_UsesPluralNounAndListsAll()
    {
        var skipped = new[]
        {
            new SkippedFile { Path = "a.txt", Reason = "reserved name" },
            new SkippedFile { Path = "b.txt", Reason = "ADS entry" },
        };

        var message = ShellResultPresenter.BuildSkippedMessage(skipped);

        message.Should().StartWith("2 entries skipped:");
        message.Should().Contain("a.txt: reserved name");
        message.Should().Contain("b.txt: ADS entry");
    }

    [Fact]
    public void BuildSkippedMessage_MoreThanMaxLines_TruncatesWithCount()
    {
        var skipped = Enumerable.Range(0, 12)
            .Select(i => new SkippedFile { Path = $"file{i}.txt", Reason = "reserved name" })
            .ToList();

        var message = ShellResultPresenter.BuildSkippedMessage(skipped, maxLinesShown: 10);

        message.Should().Contain("…and 2 more");
        message.Should().NotContain("file10.txt");
    }
}
