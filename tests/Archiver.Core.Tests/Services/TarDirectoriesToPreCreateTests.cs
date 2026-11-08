using Archiver.Core.Services;
using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

/// <summary>
/// T-F52/T-F298: which quarantine directories Pakko creates before tar.exe runs. Only those tar.exe
/// would have to create implicitly (it cannot inside the AppContainer); a directory with its own
/// entry ahead of its contents is left to tar.exe, which then sets its time.
/// </summary>
public sealed class TarDirectoriesToPreCreateTests
{
    [Theory]
    [InlineData(new[] { "a/", "a/f.txt", "b.txt" }, new string[0])]
    [InlineData(new[] { "./", "./a/", "./a/f.txt" }, new string[0])]
    [InlineData(new[] { "a/", "a/b/", "a/b/c.txt" }, new string[0])]
    [InlineData(new[] { "sub/b.txt" }, new[] { "sub" })]
    [InlineData(new[] { "a/f.txt", "a/" }, new[] { "a" })]
    [InlineData(new[] { "a/", "a/b/c.txt" }, new[] { "a/b" })]
    [InlineData(new[] { "x/y/z.txt" }, new[] { "x", "x/y" })]
    // An Extract Selected expansion of "a/b/f.txt": its folders' entries are not extracted.
    [InlineData(new[] { "a/b/f.txt" }, new[] { "a", "a/b" })]
    public void DirectoriesToPreCreate_OnlyDirectoriesWithoutAnEarlierEntry(string[] names, string[] expected)
    {
        TarExeReader.DirectoriesToPreCreate(names).Should().BeEquivalentTo(expected);
    }

    // T-F443: with a listing by best fit, folder entries come from the quarantine, without the
    // folders Pakko pre-created by listed names (case-insensitive, as NTFS names are).
    [Fact]
    public void FoldersTarExeWrote_EveryQuarantinedFolderButThePreCreated()
    {
        string accented = "caf" + (char)0xE9;
        (string, bool)[] quarantined =
        [
            ("cafe", true), (accented, true), (accented + "/x.txt", false), (accented + "/empty", true),
            ("Sub", true), ("Sub/y.txt", false), ("z.txt", false),
        ];

        TarExeReader.FoldersTarExeWrote(quarantined, new HashSet<string>(["cafe", "sub"], StringComparer.OrdinalIgnoreCase))
            .Should().Equal(accented, accented + "/empty");
    }
}
