using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F509: a path with a part that ends in a space or a dot names, by its plain form, another file
// ("a.txt " opens "a.txt"). Judged on the string as given, never through GetFullPath, which trims.
public sealed class InputPathsNameTrimmedTests
{
    [Theory]
    [InlineData(@"C:\x\a.txt ")]
    [InlineData(@"C:\x\a.txt.")]
    [InlineData(@"C:\x\d.\a.txt")]
    [InlineData(@"C:\x\d \a.txt")]
    [InlineData(@"C:\x\d.\")]
    [InlineData(@"C:\x\d.")]
    [InlineData(@"\\?\C:\x\a.txt ")]
    [InlineData(@"\\server\share\d.\a.txt")]
    [InlineData(@"\\?\UNC\server\share\a.txt.")]
    [InlineData(@"x\a.txt ")]
    public void NameTrimmed_APartEndsInASpaceOrADot_IsTrimmed(string path) =>
        InputPaths.NameTrimmed(path).Should().BeTrue();

    [Theory]
    [InlineData(@"C:\x\a.txt")]
    [InlineData(@"C:\x\.\a.txt")]
    [InlineData(@"C:\x\..\a.txt")]
    [InlineData(@"C:\x\\a.txt")]
    [InlineData(@"C:\x\d\")]
    [InlineData(@"C:\")]
    [InlineData(@"\\?\C:\")]
    [InlineData(@"\\.\C:\x\a.txt")]
    [InlineData(@"\\?\UNC\server\share\a.txt")]
    [InlineData(@"\\server\share\")]
    [InlineData(@"\\?\C:\x\a.txt")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    public void NameTrimmed_OrdinaryPaths_AreNotTrimmed(string path) =>
        InputPaths.NameTrimmed(path).Should().BeFalse();
}
