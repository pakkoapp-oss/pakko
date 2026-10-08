using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class ArchiveNamingTests
{
    [Theory]
    [InlineData(@"C:\Docs\browse_test.tar.gz", "browse_test")]
    [InlineData(@"C:\Docs\browse_test.tar.bz2", "browse_test")]
    [InlineData(@"C:\Docs\browse_test.tar.xz", "browse_test")]
    [InlineData(@"C:\Docs\browse_test.tar.zst", "browse_test")]
    [InlineData(@"C:\Docs\browse_test.tar.lzma", "browse_test")]
    [InlineData(@"C:\Docs\BROWSE_TEST.TAR.GZ", "BROWSE_TEST")]
    public void GetBaseName_CompoundTarExtension_StripsBothComponents(string archivePath, string expected)
    {
        ArchiveNaming.GetBaseName(archivePath).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\Docs\archive.zip", "archive")]
    [InlineData(@"C:\Docs\archive.7z", "archive")]
    [InlineData(@"C:\Docs\archive.rar", "archive")]
    [InlineData(@"C:\Docs\archive.tar", "archive")]
    [InlineData(@"C:\Docs\archive.tgz", "archive")]
    [InlineData(@"C:\Docs\archive.tbz2", "archive")]
    public void GetBaseName_SingleExtension_StripsLastSegmentOnly(string archivePath, string expected)
    {
        ArchiveNaming.GetBaseName(archivePath).Should().Be(expected);
    }

    [Theory]
    [InlineData(ArchiveContainerFormat.Zip, ".zip")]
    [InlineData(ArchiveContainerFormat.Tar, ".tar")]
    [InlineData(ArchiveContainerFormat.TarGz, ".tar.gz")]
    [InlineData(ArchiveContainerFormat.TarBz2, ".tar.bz2")]
    [InlineData(ArchiveContainerFormat.TarXz, ".tar.xz")]
    [InlineData(ArchiveContainerFormat.TarZst, ".tar.zst")]
    [InlineData(ArchiveContainerFormat.TarLzma, ".tar.lzma")]
    public void GetExtension_EachContainerFormat_ReturnsExpectedExtension(ArchiveContainerFormat format, string expected)
    {
        ArchiveNaming.GetExtension(format).Should().Be(expected);
    }

    [Fact]
    public void ResolveSingleArchiveName_ExplicitNameGiven_ExplicitNameWins()
    {
        ArchiveNaming.ResolveSingleArchiveName("MyArchive", [@"C:\Docs\file.txt"]).Should().Be("MyArchive");
    }

    [Fact]
    public void ResolveSingleArchiveName_ExplicitNameGiven_WinsEvenWithMultipleSources()
    {
        ArchiveNaming.ResolveSingleArchiveName("MyArchive", [@"C:\a.txt", @"C:\b.txt"]).Should().Be("MyArchive");
    }

    [Fact]
    public void ResolveSingleArchiveName_NoExplicitName_SingleSource_UsesSourceFileName()
    {
        ArchiveNaming.ResolveSingleArchiveName(null, [@"C:\Docs\report.txt"]).Should().Be("report");
    }

    [Fact]
    public void ResolveSingleArchiveName_NoExplicitName_MultipleSourcesAtDriveRoot_UsesTheDriveLetter()
    {
        // T-F281 (user decision 2026-09-30): the drive letter, as for a drive-root source itself.
        ArchiveNaming.ResolveSingleArchiveName(null, [@"C:\a.txt", @"C:\b.txt"]).Should().Be("C");
    }

    [Fact]
    public void ResolveSingleArchiveName_NoExplicitName_EmptySources_FallsBackToArchive()
    {
        ArchiveNaming.ResolveSingleArchiveName(null, []).Should().Be("archive");
    }

    [Fact]
    public void ResolveSingleArchiveName_NoExplicitName_DriveRootSource_UsesTheDriveLetter()
    {
        // T-F99/T-F281: a drive root has no file name (a single-source Drive ItemType selection via
        // the shell extension); it is named after its drive letter, not a generic "archive".
        ArchiveNaming.ResolveSingleArchiveName(null, [@"Z:\"]).Should().Be("Z");
    }

    // T-F185: a real path-traversal write was found and fixed here — an explicit ArchiveName
    // used to flow straight into Path.Combine(DestinationFolder, archiveName + extension)
    // unsanitized, so "..\..\evil" landed the created archive two directories above
    // DestinationFolder (confirmed via a real file on disk, see docs/DECISIONS.md's T-F185 entry).
    // Path.GetFileName strips any directory component down to the final segment — an archive name
    // is a bare file-name component, never a path.
    [Theory]
    [InlineData(@"..\..\evil", "evil")]
    [InlineData(@"..\..\..\deep\evil", "evil")]
    [InlineData(@"C:\Windows\System32\evil", "evil")]
    [InlineData(@"subdir\evil", "evil")]
    public void ResolveSingleArchiveName_ExplicitNameContainsPathSegments_SanitizedToFinalSegmentOnly(
        string explicitName, string expected)
    {
        ArchiveNaming.ResolveSingleArchiveName(explicitName, [@"C:\Docs\file.txt"]).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"..\")]
    [InlineData(@"\")]
    [InlineData(@"..\..\")]
    public void ResolveSingleArchiveName_ExplicitNameSanitizesToEmpty_FallsBackToArchive(string explicitName)
    {
        ArchiveNaming.ResolveSingleArchiveName(explicitName, [@"C:\Docs\file.txt"]).Should().Be("archive");
    }

    // --- T-F264: one default-name rule for the App, Explorer's Shell commands and the C++ menu ---
    // Each row mirrors a BuildAddToArchiveTitle case in ShellExtUtilsTests.cpp: the menu title
    // "Add to <name>.zip" must name the archive that is actually created.

    [Theory]
    [InlineData(new[] { @"C:\Docs\report.docx" }, "report")]
    [InlineData(new[] { @"C:\Docs\backup.tar.gz" }, "backup")]
    [InlineData(new[] { @"C:\Projects\MyStuff\first.txt", @"C:\Projects\MyStuff\second.txt" }, "MyStuff")]
    [InlineData(new[] { @"C:\first.txt", @"C:\second.txt" }, "C")]
    [InlineData(new[] { @"Z:\" }, "Z")]
    [InlineData(new[] { @"d:\" }, "D")]
    [InlineData(new[] { @"Z:" }, "Z")]
    [InlineData(new[] { @"C:\Projects\MyFolder" }, "MyFolder")]
    [InlineData(new[] { @"C:\Projects\.gitignore" }, ".gitignore")]
    [InlineData(new[] { @"\\server\share\a.txt", @"\\server\share\b.txt" }, "share")]
    [InlineData(new[] { @"\\server\share" }, "share")]
    public void GetDefaultArchiveName_MatchesTheExplorerMenuTitle(string[] sources, string expected)
    {
        ArchiveNaming.GetDefaultArchiveName(sources).Should().Be(expected);
    }

    [Fact]
    public void GetDefaultArchiveName_TrailingSeparator_UsesTheFolderName()
    {
        ArchiveNaming.GetDefaultArchiveName([@"C:\Projects\MyFolder\"]).Should().Be("MyFolder");
    }

    [Fact]
    public void GetDefaultArchiveName_NoSources_FallsBackToArchive()
    {
        ArchiveNaming.GetDefaultArchiveName([]).Should().Be("archive");
    }

    [Fact]
    public void GetDefaultArchiveName_FileNamedLikeACompoundExtension_FallsBackToArchive()
    {
        ArchiveNaming.GetDefaultArchiveName([@"C:\Docs\.tar.gz"]).Should().Be("archive");
    }

    [Fact]
    public void ResolveSingleArchiveName_NoExplicitName_UsesTheSameDefaultRule()
    {
        // Behavior change (T-F264): the App's blank name box used to give "archive" for several
        // sources and for a dotfile; it now names the archive the way Explorer does.
        ArchiveNaming.ResolveSingleArchiveName(null, [@"C:\Projects\MyStuff\a.txt", @"C:\Projects\MyStuff\b.txt"]).Should().Be("MyStuff");
        ArchiveNaming.ResolveSingleArchiveName(null, [@"C:\Projects\.gitignore"]).Should().Be(".gitignore");
        ArchiveNaming.ResolveSingleArchiveName(null, [@"C:\Docs\backup.tar.gz"]).Should().Be("backup");
    }

    [Fact]
    public void GetBaseName_ArchiveNamedOnlyByItsExtension_KeepsTheFullName()
    {
        // Same dotfile rule as the C++ title ("Extract to \".zip\\\""), instead of an empty folder name.
        ArchiveNaming.GetBaseName(@"C:\Docs\.zip").Should().Be(".zip");
    }

    // --- T-F264: the "name (N)" rule in one place ---

    [Fact]
    public void GetUniqueName_FreeName_IsReturnedUnchanged()
    {
        ArchiveNaming.GetUniqueName("report.txt", _ => false).Should().Be("report.txt");
    }

    [Fact]
    public void GetUniqueName_TakenNames_NumbersBeforeTheExtension()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "report.txt", "report (1).txt" };

        ArchiveNaming.GetUniqueName("report.txt", taken.Contains).Should().Be("report (2).txt");
    }

    // T-F362: a compound tar extension is one extension - the number goes before ".tar.gz", not
    // between ".tar" and ".gz".
    [Theory]
    [InlineData("src.tar.gz", "src (1).tar.gz")]
    [InlineData("src.tar.bz2", "src (1).tar.bz2")]
    [InlineData("src.tar.xz", "src (1).tar.xz")]
    [InlineData("src.tar.zst", "src (1).tar.zst")]
    [InlineData("src.tar.lzma", "src (1).tar.lzma")]
    [InlineData("SRC.TAR.GZ", "SRC (1).TAR.GZ")]
    [InlineData("my.backup.tar.gz", "my.backup (1).tar.gz")]
    public void GetUniqueName_CompoundTarExtension_NumbersBeforeTheWholeExtension(string name, string expected)
    {
        ArchiveNaming.GetUniqueName(name, c => c == name).Should().Be(expected);
    }

    [Theory]
    [InlineData("notes.tar.gz.txt", "notes.tar.gz (1).txt")]
    [InlineData("data.gz", "data (1).gz")]
    [InlineData("archive.tar", "archive (1).tar")]
    [InlineData(".tar.gz", ".tar (1).gz")]
    public void GetUniqueName_NotACompoundTarName_KeepsTheLastExtensionRule(string name, string expected)
    {
        ArchiveNaming.GetUniqueName(name, c => c == name).Should().Be(expected);
    }

    [Fact]
    public void GetUniqueFolderName_ExistingFolders_NumbersTheWholeName()
    {
        string parent = Path.Combine(Path.GetTempPath(), "PakkoNamingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(parent, "my.archive"));
        Directory.CreateDirectory(Path.Combine(parent, "my.archive (1)"));
        try
        {
            ArchiveNaming.GetUniqueFolderName(parent, "my.archive").Should().Be("my.archive (2)");
            ArchiveNaming.GetUniqueFolderName(parent, "fresh").Should().Be("fresh");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
