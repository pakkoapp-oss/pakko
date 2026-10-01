using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F286: a tar creation that was killed leaves its staging folder, junctions to the user's
/// folders included, in %TEMP%. The next creation removes such folders — the links only, never
/// what they point at — and leaves alone a folder whose owning process still runs.
/// </summary>
public sealed class TarCollisionStagingTests : IDisposable
{
    private const int DeadProcessId = 111;
    private const int LiveProcessId = 222;
    private readonly TempDirectory _temp = new();

    public void Dispose()
    {
        // A junction a test left in place goes first, as a link, before the folder tree is deleted.
        if (Directory.Exists(TempRoot))
        {
            foreach (string staging in Directory.GetDirectories(TempRoot))
            {
                string junction = Path.Combine(staging, "docs (1)");
                if (Directory.Exists(junction))
                    Directory.Delete(junction, recursive: false);
            }
        }
        _temp.Dispose();
    }

    private static bool IsAlive(int processId) => processId == LiveProcessId;

    private string TempRoot => Path.Combine(_temp.Path, "temp");

    private string CreateUserFolder()
    {
        string userFolder = Path.Combine(_temp.Path, "user-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(userFolder, "sub"));
        File.WriteAllText(Path.Combine(userFolder, "sub", "keep.txt"), "user data");
        return userFolder;
    }

    private string CreateStagingFolder(string name, string junctionTarget)
    {
        string staging = Path.Combine(TempRoot, name);
        Directory.CreateDirectory(staging);
        DirectoryJunction.Create(Path.Combine(staging, "docs (1)"), junctionTarget);
        File.WriteAllText(Path.Combine(staging, "copy (1).txt"), "staged copy");
        return staging;
    }

    [Fact]
    public void NewDirectoryPath_NamesTheOwningProcess()
    {
        string path = TarCollisionStaging.NewDirectoryPath(TempRoot);

        Path.GetDirectoryName(path).Should().Be(TempRoot);
        Path.GetFileName(path).Should().StartWith($"PakkoTarStage_{Environment.ProcessId}_");
        Directory.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void SweepStale_OwnerGone_RemovesFolderAndKeepsJunctionTarget()
    {
        string userFolder = CreateUserFolder();
        string staging = CreateStagingFolder($"PakkoTarStage_{DeadProcessId}_abc", userFolder);

        TarCollisionStaging.SweepStale(TempRoot, IsAlive, DateTime.UtcNow);

        Directory.Exists(staging).Should().BeFalse();
        File.ReadAllText(Path.Combine(userFolder, "sub", "keep.txt")).Should().Be("user data");
    }

    [Fact]
    public void SweepStale_OwnerStillRuns_KeepsFolder()
    {
        string userFolder = CreateUserFolder();
        string staging = CreateStagingFolder($"PakkoTarStage_{LiveProcessId}_abc", userFolder);

        TarCollisionStaging.SweepStale(TempRoot, IsAlive, DateTime.UtcNow);

        Directory.Exists(Path.Combine(staging, "docs (1)")).Should().BeTrue();
        File.Exists(Path.Combine(staging, "copy (1).txt")).Should().BeTrue();
    }

    // A folder written by a version before T-F286 names no process: only its age can tell.
    [Fact]
    public void SweepStale_NoProcessInName_RemovedOnlyOnceOld()
    {
        string userFolder = CreateUserFolder();
        string staging = CreateStagingFolder("PakkoTarStage_0123456789abcdef0123456789abcdef", userFolder);
        DateTime created = Directory.GetCreationTimeUtc(staging);

        TarCollisionStaging.SweepStale(TempRoot, IsAlive, created.AddHours(1));
        Directory.Exists(staging).Should().BeTrue();

        TarCollisionStaging.SweepStale(TempRoot, IsAlive, created.AddDays(2));
        Directory.Exists(staging).Should().BeFalse();
        File.ReadAllText(Path.Combine(userFolder, "sub", "keep.txt")).Should().Be("user data");
    }

    [Fact]
    public void SweepStale_OtherFoldersAndMissingRoot_Untouched()
    {
        TarCollisionStaging.SweepStale(Path.Combine(_temp.Path, "missing"), IsAlive, DateTime.UtcNow);

        string other = Path.Combine(TempRoot, "PakkoPreview");
        Directory.CreateDirectory(other);
        TarCollisionStaging.SweepStale(TempRoot, IsAlive, DateTime.UtcNow);

        Directory.Exists(other).Should().BeTrue();
    }
}
