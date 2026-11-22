using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F556: a path read from a listing (Explorer's selection, a drop, a picker) names the file as
// spelled; by its plain form "b.txt " opens the twin "b.txt". Listed gives such a path its \\?\ form,
// without GetFullPath (which would trim it); any other path comes back as it is, for Core to judge.
public sealed class InputPathsListedTests
{
    [Theory]
    [InlineData(@"C:\x\b.txt ", @"\\?\C:\x\b.txt ")]
    [InlineData(@"C:\x\d.", @"\\?\C:\x\d.")]
    [InlineData(@"C:\x\d.\f.txt", @"\\?\C:\x\d.\f.txt")]
    [InlineData(@"C:\x\...", @"\\?\C:\x\...")]
    [InlineData(@"\\server\share\b.txt ", @"\\?\UNC\server\share\b.txt ")]
    public void Listed_ANameWindowsTrims_IsItsLiteralForm(string path, string expected) =>
        InputPaths.Listed(path).Should().Be(expected);

    [Theory]
    [InlineData(@"\\?\C:\x\b.txt ")]
    [InlineData(@"\\?\UNC\server\share\b.txt ")]
    [InlineData(@"\\.\C:\x\b.txt ")]
    [InlineData(@"\??\C:\x\b.txt ")]
    [InlineData(@"C:\x\b.txt")]
    [InlineData(@"C:\x\d \f.txt")]
    [InlineData(@"C:\")]
    [InlineData(@"\\server\share\b.txt")]
    // Not a full path a listing gives: Core judges it as given and refuses the trimmed ones.
    [InlineData(@"x\b.txt ")]
    [InlineData(@"C:x\b.txt ")]
    [InlineData(@"\x\b.txt ")]
    [InlineData(@"C:/x/b.txt ")]
    [InlineData(@"C:\x\..\b.txt ")]
    [InlineData(@"C:\x\.\b.txt ")]
    [InlineData("C:\\x\\b\0.txt ")]
    [InlineData("")]
    public void Listed_AnyOtherPath_ComesBackAsItIs(string path) =>
        InputPaths.Listed(path).Should().Be(path);

    [Fact]
    public void Listed_IsIdempotent() =>
        InputPaths.Listed(InputPaths.Listed(@"C:\x\b.txt ")).Should().Be(@"\\?\C:\x\b.txt ");

    // The folder a listed path lies in, plain where the plain path names it: the archive's default
    // destination, shown to the user and opened in Explorer.
    [Theory]
    [InlineData(@"\\?\C:\x\b.txt ", @"C:\x")]
    [InlineData(@"\\?\UNC\server\share\x\b.txt ", @"\\server\share\x")]
    [InlineData(@"C:\x\b.txt", @"C:\x")]
    [InlineData(@"\\?\C:\x\d.\f.txt", @"\\?\C:\x\d.")]
    public void PlainParent_IsTheHoldingFolder_PlainWhereItNamesIt(string path, string expected) =>
        InputPaths.PlainParent(path).Should().Be(expected);

    [Theory]
    [InlineData(@"C:\")]
    [InlineData("")]
    public void PlainParent_OfARoot_IsNull(string path) =>
        InputPaths.PlainParent(path).Should().BeNull();
}
