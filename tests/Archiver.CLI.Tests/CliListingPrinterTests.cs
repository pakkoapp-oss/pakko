using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.CLI.Tests;

// T-F540: 'l' prints each row as the listing reads it; the header only before the first row or
// after a listing that succeeded with none; rows already printed stay when the listing fails late.
public sealed class CliListingPrinterTests
{
    private static readonly string[] Header = ["H"];

    [Fact]
    public async Task Rows_ComeAfterTheHeader_AsTheyAreRead()
    {
        (string printed, ArchiveListSummary summary, long rows) = await PrintAsync(Succeeds("a", "b"));

        printed.Should().Be("H\na\nb\n");
        summary.Success.Should().BeTrue();
        rows.Should().Be(2);
    }

    [Fact]
    public async Task NoRow_ASuccess_PrintsTheHeaderAlone()
    {
        (string printed, _, long rows) = await PrintAsync(Succeeds());

        printed.Should().Be("H\n");
        rows.Should().Be(0);
    }

    [Fact]
    public async Task NoRow_AFailure_PrintsNothing()
    {
        (string printed, ArchiveListSummary summary, _) = await PrintAsync(Fails());

        printed.Should().BeEmpty();
        summary.Success.Should().BeFalse();
    }

    // T-F545: -slt's block reads the archive's size; a listing that failed before its first row
    // (a missing archive) must not read it at all.
    [Fact]
    public async Task NoRow_AFailure_NeverMakesTheHeader()
    {
        int made = 0;
        using var output = new StringWriter();

        await CliListingPrinter.PrintAsync(Fails(), () => { made++; return Header; }, _ => true, e => [e.Path], output);

        made.Should().Be(0);
    }

    [Fact]
    public async Task AFailureAfterRows_LeavesThemPrinted()
    {
        (string printed, ArchiveListSummary summary, long rows) = await PrintAsync(Fails("a"));

        printed.Should().Be("H\na\n");
        summary.Success.Should().BeFalse();
        rows.Should().Be(1);
    }

    [Fact]
    public async Task AFilterThatTakesNoEntry_PrintsTheHeaderAtTheEnd()
    {
        (string printed, _, long rows) = await PrintAsync(Succeeds("a", "b"), includes: _ => false);

        printed.Should().Be("H\n");
        rows.Should().Be(0);
    }

    [Fact]
    public async Task NoHeader_PrintsTheRowsOnly()
    {
        (string printed, _, _) = await PrintAsync(Succeeds("a"), withHeader: false);

        printed.Should().Be("a\n");
    }

    private static async Task<(string Printed, ArchiveListSummary Summary, long Rows)> PrintAsync(
        Func<Action<ArchiveEntryInfo>, Task<ArchiveListSummary>> list, Func<ArchiveEntryInfo, bool>? includes = null, bool withHeader = true)
    {
        using var output = new StringWriter { NewLine = "\n" };
        (ArchiveListSummary summary, long rows) = await CliListingPrinter.PrintAsync(
            list, withHeader ? () => Header : null, includes ?? (_ => true), e => [e.Path], output);
        return (output.ToString(), summary, rows);
    }

    private static Func<Action<ArchiveEntryInfo>, Task<ArchiveListSummary>> Succeeds(params string[] paths) => onEntry =>
    {
        foreach (string path in paths)
            onEntry(new ArchiveEntryInfo { Path = path });
        return Task.FromResult(new ArchiveListSummary { Success = true, Count = paths.Length });
    };

    private static Func<Action<ArchiveEntryInfo>, Task<ArchiveListSummary>> Fails(params string[] paths) => onEntry =>
    {
        foreach (string path in paths)
            onEntry(new ArchiveEntryInfo { Path = path });
        return Task.FromResult(new ArchiveListSummary { Success = false, Count = paths.Length, ErrorMessage = "broken" });
    };
}
