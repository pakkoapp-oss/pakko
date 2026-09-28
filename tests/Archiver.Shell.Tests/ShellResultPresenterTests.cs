using System.Globalization;
using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.Shell.Tests;

public sealed class ShellResultPresenterTests
{
    // --- BuildSkippedMessage ---
    // T-F163: the header used to hand-roll English noun pluralization ("1 entry skipped:" / "2
    // entries skipped:"), hardcoded regardless of CurrentUICulture -- a real user on uk-UA got
    // this exact English text back after choosing Skip in T-F155's own interactive dialog. Now
    // routed through ResultMessagesLocalizer's count-suffixed, pluralization-free "Skipped (N):"
    // header (matches the WinUI App's own T-F89 "Skipped (N)" convention).

    [Fact]
    public void BuildSkippedMessage_SingleEntry_UsesLocalizedHeader()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            SkippedFile[] skipped = new[] { new SkippedFile { Path = @"C:\dir\bad.txt", Reason = "ADS entry" } };

            string message = ShellResultPresenter.BuildSkippedMessage(skipped);

            message.Should().StartWith("Skipped (1):");
            message.Should().Contain("bad.txt: ADS entry");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void BuildSkippedMessage_MultipleEntries_UsesLocalizedHeaderAndListsAll()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            SkippedFile[] skipped = new[]
            {
                new SkippedFile { Path = "a.txt", Reason = "reserved name" },
                new SkippedFile { Path = "b.txt", Reason = "ADS entry" },
            };

            string message = ShellResultPresenter.BuildSkippedMessage(skipped);

            message.Should().StartWith("Skipped (2):");
            message.Should().Contain("a.txt: reserved name");
            message.Should().Contain("b.txt: ADS entry");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void BuildSkippedMessage_MoreThanMaxLines_TruncatesWithCount()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var skipped = Enumerable.Range(0, 12)
                .Select(i => new SkippedFile { Path = $"file{i}.txt", Reason = "reserved name" })
                .ToList();

            string message = ShellResultPresenter.BuildSkippedMessage(skipped, maxLinesShown: 10);

            message.Should().Contain("…and 2 more");
            message.Should().NotContain("file10.txt");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
