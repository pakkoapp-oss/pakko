using Archiver.Core.Services.Sandbox;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

public sealed class QuarantineFilesTests
{
    [Fact]
    public void Relative_NameWindowsTrims_KeptAsWritten()
    {
        QuarantineFiles.Relative(@"C:\q\out", @"C:\q\out\d \a.txt.").Should().Be(@"d \a.txt.");
    }

    [Fact]
    public void Relative_RootWithTrailingSeparator_SameResult()
    {
        QuarantineFiles.Relative(@"C:\q\out\", @"C:\q\out\a.txt.").Should().Be("a.txt.");
    }

    [Theory]
    [InlineData(@"C:\q\other\a.txt.")]
    [InlineData(@"\\?\C:\q\out\a.txt.")]
    [InlineData(@"C:\q\outside.txt")]
    public void Relative_FileNotUnderRoot_ThrowsIOException(string file)
    {
        // T-F463 review: a normalizing fallback would turn "a.txt." into "a.txt" without a word.
        Action relative = () => QuarantineFiles.Relative(@"C:\q\out", file);

        relative.Should().Throw<IOException>();
    }

    // T-F546: archiveint.dll on ARM64 leaves a 0444 entry read-only, and tar.exe writes names Windows
    // trims; Directory.Delete alone stops at the first and a normalized path misses the second.
    [Fact]
    public void Remove_ReadOnlyFilesUnderNamesWindowsTrims_Removed()
    {
        using var temp = new TempDirectory();
        string root = Path.Combine(temp.Path, "quarantine");
        string literal = @"\\?\" + root;
        Directory.CreateDirectory(literal + @"\d \");
        foreach (string name in new[] { @"\d \x.txt", @"\a.txt.", @"\b.txt ", @"\c.txt" })
        {
            File.WriteAllText(literal + name, "x");
            File.SetAttributes(literal + name, FileAttributes.ReadOnly);
        }

        QuarantineFiles.Remove(root);

        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void Remove_RootAlreadyGone_DoesNotThrow()
    {
        using var temp = new TempDirectory();

        Action remove = () => QuarantineFiles.Remove(Path.Combine(temp.Path, "gone"));

        remove.Should().NotThrow();
    }
}
