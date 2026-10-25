using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F390: a listed source that is gone is reported by Pakko and taken off tar.exe's list, and the
/// tar.exe line that carries a wrong name for it is never shown. The stderr texts are the ones
/// measured on bsdtar 3.8.8 (docs/DECISIONS.md).
/// </summary>
public sealed class TarVanishedSourceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private TarSandboxedService.ListedSource Listed(string name, bool create)
    {
        if (create)
            File.WriteAllText(Path.Combine(_temp.Path, name), name);
        return new TarSandboxedService.ListedSource(Path.Combine(_temp.Path, name), _temp.Path, name, Path.Combine(_temp.Path, name));
    }

    // --- Happy path ---

    [Fact]
    public void ReportVanished_EverySourceExists_ChangesNothing()
    {
        List<TarSandboxedService.ListedSource> list = [Listed("a.txt", create: true), Listed("b.txt", create: true)];
        var errors = new List<ArchiveError>();

        TarSandboxedService.ReportVanished(list, errors).Should().Be(0);

        list.Should().HaveCount(2);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void DescribeCreationFailure_LinesThatNameTheRightFile_AreKept()
    {
        const string stdErr = "a src\ntar.exe: src/denied: Cannot stat: Permission denied\n" +
            "tar.exe: Couldn't open src/locked.txt: Permission denied\ntar.exe: Error exit delayed from previous errors\n";

        string text = TarSandboxedService.DescribeCreationFailure(stdErr).English;

        text.Should().Contain("src/denied: Cannot stat").And.Contain("Couldn't open src/locked.txt");
        text.Should().NotContain("a src");
    }

    // --- Security & Boundary: tar.exe's wrong name never reaches the user ---

    [Theory]
    [InlineData("tar.exe: : Couldn't visit directory: No such file or directory")]
    [InlineData(@"tar.exe: soft VS Code\bin;C:\????nope.txt: Couldn't visit directory: No such file or directory")]
    public void DescribeCreationFailure_UnvisitableLine_IsDropped(string leakingLine)
    {
        string stdErr = "a ok.txt\n" + leakingLine + "\ntar.exe: Error exit delayed from previous errors\n";

        string text = TarSandboxedService.DescribeCreationFailure(stdErr).English;

        text.Should().Be("tar.exe: Error exit delayed from previous errors");
    }

    // --- Misuse: a file named like the list's own keyword ---

    [Fact]
    public void ListedSource_NamedDashC_IsWrittenAsARelativePathAndStillFound()
    {
        TarSandboxedService.ListedSource source = Listed("-C", create: true);

        source.Lines.Should().Equal("-C", _temp.Path, "./-C");
        source.Exists().Should().BeTrue();
    }

    // --- Error path ---

    [Fact]
    public void ReportVanished_OneSourceGone_IsReportedAndTakenOffTheList()
    {
        TarSandboxedService.ListedSource gone = Listed("gone.txt", create: false);
        TarSandboxedService.ListedSource kept = Listed("kept.txt", create: true);
        List<TarSandboxedService.ListedSource> list = [gone, kept];
        var errors = new List<ArchiveError>();

        TarSandboxedService.ReportVanished(list, errors).Should().Be(1);

        list.Should().Equal(kept);
        errors.Should().ContainSingle().Which.Should().Match<ArchiveError>(
            e => e.Text!.Code == MessageCode.SourceNotFound && e.SourcePath == gone.SourcePath);
    }

    // T-F396: in 13 of 200 runs (bsdtar 3.8.8, 2026-10-25) the stale buffer overwrote the words
    // "Couldn't visit directory" too, leaving a fragment of the environment and the errno text.
    [Theory]
    [InlineData(@"tar.exe: odules;C:\Program Fi????: No such file or directory")]
    [InlineData(@"tar.exe: rogram Files\Eclipse????" +"\u0002: No such file or directory")]
    public void ReportCreationFailure_GarbledLineWithoutTheVisitWords_StillNamesTheVanishedSource(string garbledLine)
    {
        TarSandboxedService.ListedSource gone = Listed("folder", create: false);
        List<TarSandboxedService.ListedSource> list = [gone, Listed("staying.txt", create: true)];
        var errors = new List<ArchiveError>();
        string stdErr = garbledLine + "\na staying.txt\ntar.exe: Error exit delayed from previous errors\n";

        TarSandboxedService.ReportCreationFailure(stdErr, list, errors, Path.Combine(_temp.Path, "out.tar"));

        errors.Should().Contain(e => e.Text!.Code == MessageCode.SourceNotFound && e.SourcePath == gone.SourcePath);
        errors.Should().NotContain(e => e.Text!.Code == MessageCode.TarSourceUnreadable);
        errors.Should().NotContain(e => e.Message.Contains("No such file or directory"));
    }

    [Fact]
    public void ReportCreationFailure_NothingVanished_KeepsTarsMissingFileLine()
    {
        List<TarSandboxedService.ListedSource> list = [Listed("staying.txt", create: true)];
        var errors = new List<ArchiveError>();
        const string stdErr = "tar.exe: src/inner.txt: Couldn't open: No such file or directory\ntar.exe: Error exit delayed from previous errors\n";

        TarSandboxedService.ReportCreationFailure(stdErr, list, errors, Path.Combine(_temp.Path, "out.tar"));

        errors.Should().NotContain(e => e.Text!.Code == MessageCode.SourceNotFound);
        errors.Should().Contain(e => e.Message.Contains("src/inner.txt: Couldn't open"));
    }

    [Fact]
    public void ReportCreationFailure_VisitLineAndNothingVanished_SaysASourceCouldNotBeRead()
    {
        List<TarSandboxedService.ListedSource> list = [Listed("staying.txt", create: true)];
        var errors = new List<ArchiveError>();
        const string stdErr = "tar.exe: : Couldn't visit directory: No such file or directory\ntar.exe: Error exit delayed from previous errors\n";

        TarSandboxedService.ReportCreationFailure(stdErr, list, errors, Path.Combine(_temp.Path, "out.tar"));

        errors.Should().Contain(e => e.Text!.Code == MessageCode.TarSourceUnreadable);
    }

    [Fact]
    public void ReportVanished_FolderSource_CountsAsExisting()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, "folder"));
        List<TarSandboxedService.ListedSource> list = [new(Path.Combine(_temp.Path, "folder"), _temp.Path, "folder", Path.Combine(_temp.Path, "folder"))];

        TarSandboxedService.ReportVanished(list, []).Should().Be(0);
    }
}
