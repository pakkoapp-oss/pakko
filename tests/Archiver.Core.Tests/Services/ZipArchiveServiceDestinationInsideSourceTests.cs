using System.IO.Compression;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F316: the archive is written inside the folder being archived ("pakko a out.zip ."), so the
/// walk meets the run's own temporary file and chunk folder. They are not the user's files: no
/// entry, no error.
/// </summary>
public sealed class ZipArchiveServiceDestinationInsideSourceTests : IDisposable
{
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    public enum Writer
    {
        Sequential,
        ParallelByFileCount,
        HandRolledFastest,
        HandRolledPassword,
    }

    // src/ { a.txt, sub/b.txt, big.bin above the in-memory limit (chunk folder), [f000..f069.txt] }
    private string CreateSource(Writer writer)
    {
        string src = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "bravo");
        File.WriteAllBytes(Path.Combine(src, "big.bin"), new byte[1024 * 1024 + 1]);
        if (writer == Writer.ParallelByFileCount)
        {
            for (int i = 0; i < 70; i++)
                File.WriteAllText(Path.Combine(src, $"f{i:D3}.txt"), $"content {i}");
        }
        return src;
    }

    private static ArchiveOptions Options(string src, Writer writer, ArchiveMode mode) => new()
    {
        SourcePaths = [src],
        DestinationFolder = src,
        ArchiveName = mode == ArchiveMode.SingleArchive ? "out" : null,
        Mode = mode,
        CompressionLevel = writer == Writer.HandRolledFastest ? CompressionLevel.Fastest : CompressionLevel.Optimal,
        ResolvePasswordAsync = writer == Writer.HandRolledPassword
            ? _ => Task.FromResult(new PasswordDecision { Password = "s3cret-pass" })
            : null,
    };

    private static List<string> ExpectedEntries(string src) =>
        [.. Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Select(f => "src/" + Path.GetRelativePath(src, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    public static TheoryData<Writer, ArchiveMode> Cases()
    {
        var cases = new TheoryData<Writer, ArchiveMode>();
        foreach (Writer writer in Enum.GetValues<Writer>())
        {
            cases.Add(writer, ArchiveMode.SingleArchive);
            cases.Add(writer, ArchiveMode.SeparateArchives);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ArchiveAsync_DestinationInsideTheSourceFolder_PacksOnlyTheUsersFiles(Writer writer, ArchiveMode mode)
    {
        string src = CreateSource(writer);
        List<string> expected = ExpectedEntries(src);

        ArchiveResult result = await _sut.ArchiveAsync(Options(src, writer, mode));

        result.Errors.Select(e => e.Message).Should().BeEmpty();
        result.SkippedFiles.Select(s => s.Reason).Should().BeEmpty();
        result.Success.Should().BeTrue();
        string archive = result.CreatedFiles.Should().ContainSingle().Subject;
        Path.GetDirectoryName(archive).Should().Be(src);
        using ZipArchive zip = ZipFile.OpenRead(archive);
        zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).Should().Equal(expected);
        Directory.EnumerateFileSystemEntries(src, ".pakko-*").Should().BeEmpty("nothing temporary is left behind");
    }

    // Overwrite removes an old archive that lies inside the source before the walk (T-F312), so the
    // new archive does not carry the old one.
    [Fact]
    public async Task ArchiveAsync_DestinationInsideTheSourceFolder_OverwriteDoesNotPackTheOldArchive()
    {
        string src = CreateSource(Writer.Sequential);
        List<string> expected = ExpectedEntries(src);
        File.WriteAllText(Path.Combine(src, "out.zip"), "the previous archive");

        ArchiveResult result = await _sut.ArchiveAsync(
            Options(src, Writer.Sequential, ArchiveMode.SingleArchive) with { OnConflict = ConflictBehavior.Overwrite });

        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles.Single());
        zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).Should().Equal(expected);
    }
}
