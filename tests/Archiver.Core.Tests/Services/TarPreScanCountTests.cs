using System.Runtime.Versioning;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F237 (tar pre-scan on the shared walker): CountRecursiveEntriesAndBytes also refuses names
// tar.exe cannot receive (T-F266). One unreadable folder used to end the whole enumeration, so
// every name after it went unchecked; a reparse point's own name must be checked too, because
// tar.exe is handed that name even though the walk does not enter it.
[SupportedOSPlatform("windows")]
public sealed class TarPreScanCountTests : IDisposable
{
    // U+2713 CHECK MARK: in no ANSI code page, and the exact name that crashed tar.exe (T-F266).
    private static readonly string CheckMark = char.ConvertFromUtf32(0x2713);
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Count_UnreadableFolderBeforeUnrepresentableName_NameStillFlagged()
    {
        string source = Path.Combine(_temp.Path, "src");
        string locked = Path.Combine(source, "a_locked");
        Directory.CreateDirectory(locked);
        string bad = Path.Combine(source, "z_" + CheckMark);
        Directory.CreateDirectory(bad);

        string? unrepresentable;
        using (new DeniedFolder(locked))
            (_, _, unrepresentable) = TarSandboxedService.CountRecursiveEntriesAndBytes(source);

        unrepresentable.Should().Be(bad);
    }

    [Fact]
    public void Count_JunctionWithUnrepresentableName_FlaggedAndNotEntered()
    {
        string source = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.txt"), "abc");
        string outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "big.bin"), new string('x', 1000));
        string link = Path.Combine(source, "link_" + CheckMark);
        if (!ZipArchiveServiceArchiveTests.TryCreateJunction(link, outside))
            return; // junctions not supported on this system

        try
        {
            (long entries, long bytes, string? unrepresentable) = TarSandboxedService.CountRecursiveEntriesAndBytes(source);

            unrepresentable.Should().Be(link);
            entries.Should().Be(3); // src, a.txt, the junction itself
            bytes.Should().Be(3);
        }
        finally
        {
            Directory.Delete(link, recursive: false);
        }
    }

    [Fact]
    public void Count_PlainTree_CountsRootFoldersAndFiles()
    {
        string source = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(Path.Combine(source, "sub", "empty"));
        File.WriteAllText(Path.Combine(source, "a.txt"), "12");
        File.WriteAllText(Path.Combine(source, "sub", "b.txt"), "345");

        (long entries, long bytes, string? unrepresentable) = TarSandboxedService.CountRecursiveEntriesAndBytes(source);

        entries.Should().Be(5);
        bytes.Should().Be(5);
        unrepresentable.Should().BeNull();
    }
}
