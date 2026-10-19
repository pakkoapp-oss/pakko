using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using FluentAssertions;

namespace Archiver.CLI.Tests.Adversarial;

/// <summary>
/// T-F420 independent review: twins Windows writes as one name, met across archives of one
/// extraction (the router's shared registry), and against a file already on disk.
/// </summary>
public sealed class IndependentReviewTests : IDisposable
{
    private readonly AdversarialScratch _scratch = new();

    public void Dispose() => _scratch.Dispose();

    private string[] Contents(string relative) =>
        [.. _scratch.FilesUnder(relative).Select(name => File.ReadAllText(@"\\?\" + Path.Combine(_scratch.Inside(relative), name))).Order(StringComparer.Ordinal)];

    private string TarGz(string relative, params (string Name, string Content)[] entries)
    {
        string path = _scratch.Inside(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (FileStream file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            foreach ((string name, string content) in entries)
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                });
            }
        }
        return path;
    }

    // ZIP's "a.txt" then a tar's "a.txt." in one "x arcs\*": ZIP -> ZIP keeps both (named
    // differently), but the tar move phase claims with namedDifferently: false and its twin list
    // covers its own archive only, so -y overwrites the ZIP's file, exit 0.
    [Theory]
    [InlineData("a.txt.")]
    [InlineData("a.txt ")]
    public void ZipThenTar_TrimTwinAcrossArchives_Y_BothKeptOrNotExitZero(string twin)
    {
        _scratch.Zip(@"arcs\a.zip", ("a.txt", "ZIP"));
        TarGz(@"arcs\b.tar.gz", (twin, "TAR"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", @"arcs\*", "-oout", "-y");

        string[] contents = Contents("out");
        if (exitCode == 0)
            contents.Should().Contain("ZIP", $"exit 0 means every entry is on disk; files: {string.Join(", ", _scratch.FilesUnder("out"))}; stderr: {stdErr}")
                .And.Contain("TAR");
    }

    // The same across two tar archives.
    [Fact]
    public void TarThenTar_TrimTwinAcrossArchives_Y_BothKeptOrNotExitZero()
    {
        TarGz(@"arcs\a.tar.gz", ("a.txt", "FIRST"));
        TarGz(@"arcs\b.tar.gz", ("a.txt.", "SECOND"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", @"arcs\*", "-oout", "-y");

        string[] contents = Contents("out");
        if (exitCode == 0)
            contents.Should().Contain("FIRST", $"files: {string.Join(", ", _scratch.FilesUnder("out"))}; stderr: {stdErr}")
                .And.Contain("SECOND");
    }

    // Control: ZIP -> ZIP across archives is kept apart.
    [Fact]
    public void ZipThenZip_TrimTwinAcrossArchives_Y_BothKept()
    {
        _scratch.Zip(@"arcs\a.zip", ("a.txt", "FIRST"));
        _scratch.Zip(@"arcs\b.zip", ("a.txt.", "SECOND"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", @"arcs\*", "-oout", "-y");

        exitCode.Should().Be(0, stdErr);
        Contents("out").Should().Equal("FIRST", "SECOND");
    }

    // The ZIP route keeps an entry named as another's 8.3 alias apart (GetFullPath expands it);
    // on the tar route tar.exe writes the alias over the long name inside the quarantine, before
    // Pakko sees a file, so the archive is refused whole (nothing written, not deletable) - the
    // review's "the first survives" asked for one of the two outcomes that lose nothing.
    [RequiresShortNamesTheory]
    [InlineData(null)]
    [InlineData("-y")]
    public void TarGz_EntryNamedAsAnotherEntrysShortName_RefusedOrBothSurvive(string? overwrite)
    {
        TarGz("arc.tar.gz", ("longfilename.txt", "LONG"), ("LONGFI~1.TXT", "SHORT"));
        string[] args = overwrite is null ? ["x", "arc.tar.gz", "-oout"] : ["x", "arc.tar.gz", "-oout", overwrite];

        (int exitCode, _, string stdErr) = _scratch.Pakko(args);

        string[] contents = Directory.Exists(_scratch.Inside("out")) ? Contents("out") : [];
        string because = $"files: {string.Join(", ", contents)}; exit {exitCode}; stderr: {stdErr}";
        if (contents.Length == 0)
            exitCode.Should().Be(2, because);
        else
            contents.Should().Contain("LONG", because).And.Contain("SHORT");
    }

    // Found fixing the above: the move phase took a quarantined file's path through
    // Path.GetRelativePath, which reads "a.txt." as "a.txt" - so an entry left out by -x was still
    // extracted.
    [Fact]
    public void TarGz_TrailingDotEntryExcluded_IsNotExtracted()
    {
        TarGz("arc.tar.gz", ("a.txt.", "DOT"), ("b.txt", "B"));

        (int exitCode, _, string stdErr) = _scratch.Pakko("x", "arc.tar.gz", "-oout", "-x!a.txt.");

        exitCode.Should().Be(0, stdErr);
        Contents("out").Should().Equal("B");
    }

    // The tar route of Zip_TrailingDotEntry_OverAnUnrelatedExistingFile_Y_UserFileStays.
    [Theory]
    [InlineData("a.txt.")]
    [InlineData("a.txt ")]
    public void TarGz_TrailingDotEntry_OverAnUnrelatedExistingFile_Y_UserFileStays(string entry)
    {
        TarGz("arc.tar.gz", (entry, "ENTRY"));
        _scratch.WriteFile(@"out\a.txt", "USER");

        _scratch.Pakko("x", "arc.tar.gz", "-oout", "-y");

        Contents("out").Should().Contain("USER").And.Contain("ENTRY");
    }

    // 7za 26.x (the vendored copy, measured): "a.txt." is written as "a.txt_" and the user's
    // a.txt stays. Pakko writes it as "a.txt", so -y replaces a file the archive never named.
    [Theory]
    [InlineData("a.txt.")]
    [InlineData("a.txt ")]
    public void Zip_TrailingDotEntry_OverAnUnrelatedExistingFile_Y_UserFileStays(string entry)
    {
        _scratch.Zip("arc.zip", (entry, "ENTRY"));
        _scratch.WriteFile(@"out\a.txt", "USER");

        _scratch.Pakko("x", "arc.zip", "-oout", "-y");

        Contents("out").Should().Contain("USER", "7-Zip writes the entry as a.txt_; the user's a.txt is not the entry's name")
            .And.Contain("ENTRY");
    }
}
