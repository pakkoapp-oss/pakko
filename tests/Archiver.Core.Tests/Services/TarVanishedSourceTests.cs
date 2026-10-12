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
        return new TarSandboxedService.ListedSource(Path.Combine(_temp.Path, name), _temp.Path, name);
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

    [Fact]
    public void ReportVanished_FolderSource_CountsAsExisting()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, "folder"));
        List<TarSandboxedService.ListedSource> list = [new(Path.Combine(_temp.Path, "folder"), _temp.Path, "folder")];

        TarSandboxedService.ReportVanished(list, []).Should().Be(0);
    }
}
