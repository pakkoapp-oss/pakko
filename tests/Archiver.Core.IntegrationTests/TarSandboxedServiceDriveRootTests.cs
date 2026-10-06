using System.Diagnostics;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F285: tar.exe cannot visit a drive root (as an argument, or as "." under "-C X:\"), and on a
/// <c>subst</c> drive it cannot archive a file that sits directly in the root. A <c>subst</c> drive
/// over a temp folder stands in for the drive; a real volume root cannot be archived in a test.
/// </summary>
[Collection("TarSandbox")]
public sealed class TarSandboxedServiceDriveRootTests : IDisposable
{
    private readonly TarSandboxedService _sut = new(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();
    private readonly string _content;
    private readonly string _drive;

    public TarSandboxedServiceDriveRootTests()
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

    private Task<ArchiveResult> CompressAsync(string source) => _sut.CompressAsync(new ArchiveOptions
    {
        SourcePaths = [source],
        DestinationFolder = _temp.Path,
        ArchiveName = "out",
        Format = ArchiveContainerFormat.Tar,
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
