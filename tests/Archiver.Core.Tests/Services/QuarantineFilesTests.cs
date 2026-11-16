using Archiver.Core.Services.Sandbox;
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
}
