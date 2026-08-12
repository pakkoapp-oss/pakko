using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F166: direct coverage for ArchiveEntrySecurity.PathContainsReparsePoint using a real NTFS
/// junction (IO_REPARSE_TAG_MOUNT_POINT) as an intermediate directory component — the extraction
/// pipeline (ZipArchiveService.TryExtractSingleEntryAsync, T-F37) calls this exact function to
/// reject a destination path that traverses a reparse point anywhere between the entry's file and
/// the extraction root. Prior coverage (T-F23) only ever exercised symlinks
/// (IO_REPARSE_TAG_SYMLINK); the generic FileAttributes.ReparsePoint check this function uses is
/// tag-agnostic, but was never actually proven against a junction specifically until this file.
/// </summary>
public sealed class ArchiveEntrySecurityReparsePointTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void PathContainsReparsePoint_JunctionAsIntermediateComponent_ReturnsTrue()
    {
        string root = Path.Combine(_temp.Path, "root");
        Directory.CreateDirectory(root);
        string realTargetDir = Path.Combine(_temp.Path, "outside_target");
        Directory.CreateDirectory(realTargetDir);
        string junctionDir = Path.Combine(root, "sub");

        if (!ZipArchiveServiceArchiveTests.TryCreateJunction(junctionDir, realTargetDir))
            return; // junctions not supported on this system — skip

        try
        {
            string destFilePath = Path.Combine(junctionDir, "evil.txt");

            bool result = ArchiveEntrySecurity.PathContainsReparsePoint(destFilePath, root);

            result.Should().BeTrue();
        }
        finally
        {
            // See ZipArchiveServiceArchiveTests.ArchiveAsync_DirectoryWithJunction_... for why
            // the junction must be removed (non-recursive) before TempDirectory.Dispose runs.
            Directory.Delete(junctionDir, recursive: false);
        }
    }

    [Fact]
    public void PathContainsReparsePoint_NoReparsePointInChain_ReturnsFalse()
    {
        string root = Path.Combine(_temp.Path, "root");
        string sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        string destFilePath = Path.Combine(sub, "plain.txt");

        bool result = ArchiveEntrySecurity.PathContainsReparsePoint(destFilePath, root);

        result.Should().BeFalse();
    }
}
