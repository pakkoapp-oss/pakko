using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

public sealed class QuarantineStagingTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PakkoQuarantineStagingTests_" + Guid.NewGuid());

    public QuarantineStagingTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void IsSameVolume_TwoPathsOnSameDrive_ReturnsTrue()
    {
        string pathA = Path.Combine(_tempDir, "a.txt");
        string pathB = Path.Combine(_tempDir, "sub", "b.txt");

        QuarantineStaging.IsSameVolume(pathA, pathB).Should().BeTrue();
    }

    [Fact]
    public void IsSameVolume_DifferentDriveLetters_ReturnsFalse()
    {
        QuarantineStaging.IsSameVolume(@"C:\some\path.txt", @"D:\other\path.txt").Should().BeFalse();
    }

    [Fact]
    public void StageArchive_SameVolume_CreatesHardLink()
    {
        string source = Path.Combine(_tempDir, "source.tar");
        File.WriteAllText(source, "archive contents");
        string dest = Path.Combine(_tempDir, "staged.tar");

        QuarantineStaging.StageArchive(source, dest);

        File.Exists(dest).Should().BeTrue();
        File.ReadAllText(dest).Should().Be("archive contents");

        // A hardlink shares the same underlying file — writing through the original path must
        // be visible through the staged path too. This is what distinguishes a real hardlink
        // from a copy-that-happened-to-succeed.
        File.AppendAllText(source, " appended");
        File.ReadAllText(dest).Should().Be("archive contents appended");
    }

    [Fact]
    public void StageArchive_DestinationAlreadyExists_Throws()
    {
        string source = Path.Combine(_tempDir, "source.tar");
        File.WriteAllText(source, "contents");
        string dest = Path.Combine(_tempDir, "staged.tar");
        File.WriteAllText(dest, "already here");

        Action act = () => QuarantineStaging.StageArchive(source, dest);

        act.Should().Throw<IOException>();
    }
}
