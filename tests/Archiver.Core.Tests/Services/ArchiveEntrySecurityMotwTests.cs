using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F358: the archive's mark is read once (<see cref="ArchiveEntrySecurity.ReadMotw"/>) and
/// written per file (<see cref="ArchiveEntrySecurity.TryWriteMotw"/>); what each file ends up
/// with is what <see cref="ArchiveEntrySecurity.TryPropagateMotw"/> gave before (T-F45, T-F51).
/// </summary>
public sealed class ArchiveEntrySecurityMotwTests : IDisposable
{
    private const string Mark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.test/a.zip\r\n";
    private static readonly DateTime Earlier = new(2019, 3, 4, 10, 20, 30, DateTimeKind.Utc);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string MarkedArchive()
    {
        string archive = _temp.CreateFile("a.zip");
        File.WriteAllText(archive + ":Zone.Identifier", Mark);
        return archive;
    }

    private static string? MarkOn(string file) =>
        File.Exists(file + ":Zone.Identifier") ? File.ReadAllText(file + ":Zone.Identifier") : null;

    // --- Happy path ---

    [Theory]
    [InlineData(MotwMode.AllFiles)]
    [InlineData(MotwMode.UnsafeExtensionsOnly)]
    public void ReadMotw_MarkedArchive_ReturnsItsBytes(MotwMode mode)
    {
        ArchiveEntrySecurity.ReadMotw(MarkedArchive(), mode).Should().Equal(System.Text.Encoding.UTF8.GetBytes(Mark));
    }

    [Fact]
    public void TryWriteMotw_AllFiles_WritesTheMarkOnAnyFile()
    {
        string file = _temp.CreateFile("readme.txt");

        ArchiveEntrySecurity.TryWriteMotw(ArchiveEntrySecurity.ReadMotw(MarkedArchive(), MotwMode.AllFiles)!, file, MotwMode.AllFiles);

        MarkOn(file).Should().Be(Mark);
    }

    [Fact]
    public void TryPropagateMotw_MarkedArchive_WritesTheMarkAndKeepsTheFilesTime()
    {
        string file = _temp.CreateFile("readme.txt");
        File.SetLastWriteTimeUtc(file, Earlier);

        ArchiveEntrySecurity.TryPropagateMotw(MarkedArchive(), file);

        MarkOn(file).Should().Be(Mark);
        File.GetLastWriteTimeUtc(file).Should().Be(Earlier);
    }

    // --- Security & boundary ---

    [Fact]
    public void ReadMotw_Disabled_IsNullEvenForAMarkedArchive()
    {
        ArchiveEntrySecurity.ReadMotw(MarkedArchive(), MotwMode.Disabled).Should().BeNull();
    }

    [Theory]
    [InlineData("payload.exe", true)]
    [InlineData("PAYLOAD.EXE", true)]
    [InlineData("run.ps1", true)]
    [InlineData("link.lnk", true)]
    [InlineData("readme.txt", false)]
    [InlineData("noextension", false)]
    public void TryWriteMotw_UnsafeExtensionsOnly_MarksOnlyTheListedExtensions(string name, bool marked)
    {
        string file = _temp.CreateFile(name);

        ArchiveEntrySecurity.TryWriteMotw([1, 2, 3], file, MotwMode.UnsafeExtensionsOnly);

        File.Exists(file + ":Zone.Identifier").Should().Be(marked);
    }

    [Fact]
    public void TryWriteMotw_FileAlreadyMarked_ReplacesTheMark()
    {
        string file = _temp.CreateFile("a.txt");
        File.WriteAllText(file + ":Zone.Identifier", "a longer mark than the new one, left by something else");

        ArchiveEntrySecurity.TryWriteMotw(System.Text.Encoding.UTF8.GetBytes(Mark), file, MotwMode.AllFiles);

        MarkOn(file).Should().Be(Mark);
    }

    [Fact]
    public void TryWriteMotw_WhileTheFileIsOpenForWriting_Succeeds()
    {
        // What extraction does: the mark goes on before the file's one close.
        string file = Path.Combine(_temp.Path, "open.txt");
        using (var content = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            content.Write([1, 2, 3]);
            content.Flush();

            ArchiveEntrySecurity.TryWriteMotw(System.Text.Encoding.UTF8.GetBytes(Mark), file, MotwMode.AllFiles);
        }

        MarkOn(file).Should().Be(Mark);
        File.ReadAllBytes(file).Should().Equal(1, 2, 3);
    }

    // --- Misuse & error path ---

    [Fact]
    public void ReadMotw_ArchiveWithoutAMark_IsNull()
    {
        ArchiveEntrySecurity.ReadMotw(_temp.CreateFile("plain.zip"), MotwMode.AllFiles).Should().BeNull();
    }

    [Theory]
    [InlineData(@"C:\no-such-folder-pakko\a.zip")]
    [InlineData("")]
    [InlineData("a\0b.zip")]
    public void ReadMotw_ArchiveThatCannotBeRead_IsNullAndDoesNotThrow(string archivePath)
    {
        ArchiveEntrySecurity.ReadMotw(archivePath, MotwMode.AllFiles).Should().BeNull();
    }

    [Fact]
    public void TryWriteMotw_FileThatDoesNotExistInAMissingFolder_DoesNotThrow()
    {
        Action act = () => ArchiveEntrySecurity.TryWriteMotw([1], Path.Combine(_temp.Path, "missing", "a.exe"), MotwMode.AllFiles);

        act.Should().NotThrow();
    }

    [Fact]
    public void TryPropagateMotw_ArchiveWithoutAMark_LeavesTheFileUnmarked()
    {
        string file = _temp.CreateFile("a.exe");

        ArchiveEntrySecurity.TryPropagateMotw(_temp.CreateFile("plain.zip"), file);

        File.Exists(file + ":Zone.Identifier").Should().BeFalse();
    }
}
