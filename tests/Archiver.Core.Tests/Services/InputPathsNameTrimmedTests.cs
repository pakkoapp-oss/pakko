using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F509: a path with a part Windows changes names, by its plain form, another file ("a.txt " opens
// "a.txt"). Windows drops every trailing dot and space of the last part, but of a part in the middle
// only one trailing dot ("d." is "d"; "d ", "d.." and "d. " stay), measured with GetFullPath.
// Judged part by part on the string as given: GetFullPath of the whole path would already have trimmed it.
public sealed class InputPathsNameTrimmedTests
{
    [Theory]
    [InlineData(@"C:\x\a.txt ")]
    [InlineData(@"C:\x\a.txt.")]
    [InlineData(@"C:\x\d.\a.txt")]
    [InlineData(@"C:\x\d .\a.txt")]
    [InlineData(@"C:\x\d ")]
    [InlineData(@"C:\x\...")]
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
    [InlineData(@"C:\x\d \a.txt")]
    [InlineData(@"C:\x\d \")]
    [InlineData(@"C:\x\d..\a.txt")]
    [InlineData(@"C:\x\d. \a.txt")]
    [InlineData(@"C:\x\...\a.txt")]
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
