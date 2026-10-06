using System.Diagnostics;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F285: tar.exe cannot visit a drive root (as an argument, or as "." under "-C X:\"), and on a
/// <c>subst</c> drive it cannot archive a file that sits directly in the root. A <c>subst</c> drive
/// over a temp folder stands in for the drive; a real volume root cannot be archived in a test.
/// T-F344: the ZIP engine named a drive root's entries with a leading "/", which Pakko itself
/// refuses to extract.
/// </summary>
[Collection("TarSandbox")]
public sealed class DriveRootSourceTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();
    private readonly string _content;
    private readonly string _drive;

    public DriveRootSourceTests()
    {
        _content = Path.Combine(_temp.Path, "content");
        Directory.CreateDirectory(Path.Combine(_content, "sub"));
        File.WriteAllText(Path.Combine(_content, "r.txt"), "root file");
        File.WriteAllText(Path.Combine(_content, "sub", "s.txt"), "sub file");
        _drive = FreeDriveLetter() + ":";
        Subst(_drive, _content);
    }

    public void Dispose()
    {
        Subst(_drive, "/D");
        _temp.Dispose();
    }

    [Integration]
    public async Task CompressAsync_SubstDriveRoot_ArchivesWhatTheRootHolds()
    {
        ArchiveResult result = await CompressAsync(_drive + @"\");

        result.Errors.Should().BeEmpty();
        string destDir = await ExtractAsync(result.CreatedFiles.Single());
        File.ReadAllText(Path.Combine(destDir, "r.txt")).Should().Be("root file");
        File.ReadAllText(Path.Combine(destDir, "sub", "s.txt")).Should().Be("sub file");
    }

    [Integration]
    public async Task CompressAsync_FileInSubstDriveRoot_IsArchived()
    {
        ArchiveResult result = await CompressAsync(_drive + @"\r.txt");

        result.Errors.Should().BeEmpty();
        string destDir = await ExtractAsync(result.CreatedFiles.Single());
        File.ReadAllText(Path.Combine(destDir, "r.txt")).Should().Be("root file");
    }

    // Every NTFS volume has "System Volume Information" in its root, which tar.exe cannot read
    // ("Cannot stat: Permission denied", exit 1): a named entry the user never asked for.
    [Integration]
    public async Task CompressAsync_DriveRoot_LeavesOutHiddenSystemEntries()
    {
        string systemFile = Path.Combine(_content, "pagefile.sys");
        File.WriteAllText(systemFile, "os");
        File.SetAttributes(systemFile, FileAttributes.Hidden | FileAttributes.System);
        string hiddenOnly = Path.Combine(_content, "hidden.txt");
        File.WriteAllText(hiddenOnly, "mine");
        File.SetAttributes(hiddenOnly, FileAttributes.Hidden);

        ArchiveResult result = await CompressAsync(_drive + @"\");

        result.Errors.Should().BeEmpty();
        string destDir = await ExtractAsync(result.CreatedFiles.Single());
        File.Exists(Path.Combine(destDir, "pagefile.sys")).Should().BeFalse();
        File.Exists(Path.Combine(destDir, "hidden.txt")).Should().BeTrue("only the OS's own entries are left out");
    }

    [Integration]
    public async Task CompressAsync_DriveRoot_SkipsAJunctionInTheRootAsItDoesASelectedOne()
    {
        string outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "not in the drive");
        DirectoryJunction.Create(Path.Combine(_content, "link"), outside);

        ArchiveResult result = await CompressAsync(_drive + @"\");

        result.Errors.Should().BeEmpty();
        result.SkippedFiles.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.LinkNotArchived);
        string destDir = await ExtractAsync(result.CreatedFiles.Single());
        Directory.Exists(Path.Combine(destDir, "link")).Should().BeFalse();
        File.Exists(Path.Combine(destDir, "r.txt")).Should().BeTrue();
    }

    // Explorer's "Add to X.tar" on a drive writes the archive into that drive's root.
    [Integration]
    public async Task CompressAsync_DriveRootWithTheArchiveInIt_PacksNeitherTheArchiveNorItsTempFile()
    {
        ArchiveResult first = await CompressAsync(_drive + @"\", destinationFolder: _drive + @"\");
        ArchiveResult second = await CompressAsync(_drive + @"\", destinationFolder: _drive + @"\");

        first.Errors.Should().BeEmpty();
        second.Errors.Should().BeEmpty("the first run's archive is replaced, not packed or missed");
        string destDir = await ExtractAsync(second.CreatedFiles.Single());
        Directory.GetFileSystemEntries(destDir).Select(Path.GetFileName).Should().BeEquivalentTo(["r.txt", "sub"]);
    }

    // 3 files take the sequential ZIP writer, 70 the parallel one (64-file threshold).
    [Theory]
    [InlineData(0)]
    [InlineData(70)]
    public async Task ZipArchiveAsync_DriveRoot_NamesEntriesWithoutALeadingSlash(int extraFiles)
    {
        for (int i = 0; i < extraFiles; i++)
            File.WriteAllText(Path.Combine(_content, "sub", $"f{i}.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_content, "empty"));
        var zip = new ZipArchiveService(new GroupPolicyOptions());

        ArchiveResult created = await zip.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [_drive + @"\"],
            DestinationFolder = _temp.Path,
            ArchiveName = "out",
        });

        created.Errors.Should().BeEmpty();
        using (System.IO.Compression.ZipArchive archive = System.IO.Compression.ZipFile.OpenRead(created.CreatedFiles.Single()))
        {
            archive.Entries.Select(e => e.FullName).Should().Contain(["r.txt", "sub/s.txt", "empty/"])
                .And.NotContain(name => name.StartsWith('/'));
        }
        string destDir = Path.Combine(_temp.Path, "zip-extract");
        ArchiveResult extracted = await zip.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [created.CreatedFiles.Single()],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });
        extracted.Errors.Should().BeEmpty();
        File.ReadAllText(Path.Combine(destDir, "sub", "s.txt")).Should().Be("sub file");
    }

    private Task<ArchiveResult> CompressAsync(string source, string? destinationFolder = null) => _sut.CompressAsync(new ArchiveOptions
    {
        SourcePaths = [source],
        DestinationFolder = destinationFolder ?? _temp.Path,
        ArchiveName = "out",
        Format = ArchiveContainerFormat.Tar,
        OnConflict = ConflictBehavior.Overwrite,
    });

    private async Task<string> ExtractAsync(string archivePath)
    {
        string destDir = Path.Combine(_temp.Path, "extract");
        ArchiveResult result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archivePath],
            DestinationFolder = destDir,
            Mode = ExtractMode.SingleFolder,
        });
        result.Success.Should().BeTrue(because: string.Join("; ", result.Errors.Select(e => e.Message)));
        return destDir;
    }

    private static char FreeDriveLetter()
    {
        HashSet<char> used = [.. DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0]))];
        for (char letter = 'Z'; letter >= 'G'; letter--)
        {
            if (!used.Contains(letter))
                return letter;
        }
        throw new InvalidOperationException("No free drive letter for subst.");
    }

    private static void Subst(string drive, string argument)
    {
        using Process process = Process.Start(new ProcessStartInfo(@"C:\Windows\System32\subst.exe")
        {
            ArgumentList = { drive, argument },
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"subst {drive} {argument} exited {process.ExitCode}");
    }
}
