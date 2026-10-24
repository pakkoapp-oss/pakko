using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using FluentAssertions;

namespace Archiver.CLI.Tests.Subprocess;

/// <summary>
/// T-F412: "l -slt" through the real exe, and T-F418: a tar name with a backslash listed once,
/// selected and filtered by that name.
/// </summary>
public sealed class CliTechListingSubprocessTests
{
    [Fact]
    public void Slt_OnAZip_PrintsSevenZipsBlocks()
    {
        string zip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "s.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("src/");
            using var writer = new StreamWriter(archive.CreateEntry("src/a.txt", CompressionLevel.NoCompression).Open());
            writer.Write("hello");
        }

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("l", "-slt", zip);

        exitCode.Should().Be(0, stdErr);
        string[] lines = stdOut.Split(["\r\n", "\n"], StringSplitOptions.None);
        lines.Should().ContainInOrder("--", "Path = " + zip, "Type = zip", "Physical Size = " + new FileInfo(zip).Length, "", "----------");
        lines.Should().ContainInOrder(@"Path = src\a.txt", "Folder = -", "Size = 5", "Packed Size = 5");
        lines.Should().Contain(["CRC = 3610A686", "Encrypted = -", "Folder = +"]);
    }

    [Fact]
    public void Slt_WithBa_PrintsOnlyTheEntryBlocks()
    {
        string zip = Path.Combine(CliFixtureFiles.CreateScratchDir(), "s.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("a.txt");

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("l", "-slt", "-ba", zip);

        exitCode.Should().Be(0, stdErr);
        stdOut.Should().StartWith("Path = a.txt").And.NotContain("----------").And.NotContain("Listing archive");
    }

    [RequiresTarExe]
    public void Slt_OnATarGz_NamesGzip_AndListsTheTarsFiles()
    {
        string tgz = Path.Combine(CliFixtureFiles.CreateScratchDir(), "s.tar.gz");
        WriteTar(tgz, gzip: true, ("sub/b.txt", "world"));

        (int exitCode, string stdOut, string stdErr) = CliProcessRunner.Run("l", "-slt", tgz);

        exitCode.Should().Be(0, stdErr);
        stdOut.Should().Contain("Type = gzip").And.Contain(@"Path = sub\b.txt").And.NotContain("Packed Size");
    }

    [Fact]
    public void Slt_OnExtract_IsExitSeven()
    {
        (int exitCode, _, string stdErr) = CliProcessRunner.Run("x", "nothing.zip", "-slt");

        exitCode.Should().Be(7);
        stdErr.Should().Contain("-slt");
    }

    // T-F418: bsdtar lists "a\b.txt" as "a\\b.txt"; Pakko lists, filters and extracts by the name itself.
    [RequiresTarExe]
    public void List_TarNameWithABackslash_ShowsItOnce_AndAFilterNamingItTakesOnlyIt()
    {
        string tar = Path.Combine(CliFixtureFiles.CreateScratchDir(), "b.tar");
        WriteTar(tar, gzip: false, ("a\\b.txt", "backslash"), ("ab.txt", "plain"));
        string dest = Path.Combine(CliFixtureFiles.CreateScratchDir(), "out");

        (int listExit, string listed, string listErr) = CliProcessRunner.Run("l", "-ba", tar);
        (int extractExit, _, string extractErr) = CliProcessRunner.Run("x", tar, $"-o{dest}", "a\\b.txt", "-y");

        listExit.Should().Be(0, listErr);
        listed.Should().Contain("\ta\\b.txt").And.NotContain("a\\\\b.txt");
        extractExit.Should().Be(0, extractErr);
        File.ReadAllText(Path.Combine(dest, "a", "b.txt")).Should().Be("backslash");
        File.Exists(Path.Combine(dest, "ab.txt")).Should().BeFalse();
    }

    private static void WriteTar(string path, bool gzip, params (string Name, string Content)[] entries)
    {
        using FileStream file = File.Create(path);
        using Stream stream = gzip ? new GZipStream(file, CompressionLevel.Fastest) : file;
        using var writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: true);
        foreach ((string name, string content) in entries)
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.ASCII.GetBytes(content)) });
    }
}
