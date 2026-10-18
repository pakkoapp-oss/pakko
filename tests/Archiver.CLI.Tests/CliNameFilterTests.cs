using System.Diagnostics;
using FluentAssertions;

namespace Archiver.CLI.Tests;

/// <summary>
/// T-F410: every row was measured with the vendored 7za.exe 26.02 ("x t.zip -oo -bb1 ..." printed
/// exactly these entries) on an archive holding <see cref="Entries"/>; see docs/DECISIONS.md's
/// T-F410 entry.
/// </summary>
public sealed class CliNameFilterTests
{
    private static readonly (string Path, bool IsFolder)[] Entries =
    [
        ("a.txt", false), ("b.nsis", false), ("dir/", true), ("dir/c.txt", false), ("dir/sub/", true),
        ("dir/sub/d.nsis", false), ("dir/sub/e.TXT", false), ("dir/sub/y.nsis", false), ("emptydir/", true),
        ("Other/", true), ("Other/dir/", true), ("Other/dir/x.nsis", false), ("Other/f.txt", false),
    ];

    private const string All =
        "a.txt b.nsis dir/ dir/c.txt dir/sub/ dir/sub/d.nsis dir/sub/e.TXT dir/sub/y.nsis emptydir/ Other/ Other/dir/ Other/dir/x.nsis Other/f.txt";

    // Masks: "+name" after the archive or -i!, "-name" -x!; a trailing "|r", "|r0" recurses it.
    [Theory]
    [InlineData("+dir", "dir/ dir/c.txt dir/sub/ dir/sub/d.nsis dir/sub/e.TXT dir/sub/y.nsis")]
    [InlineData("+DIR", "dir/ dir/c.txt dir/sub/ dir/sub/d.nsis dir/sub/e.TXT dir/sub/y.nsis")]
    [InlineData("+dir\\", "dir/ dir/c.txt dir/sub/ dir/sub/d.nsis dir/sub/e.TXT dir/sub/y.nsis")]
    [InlineData("+dir/sub", "dir/sub/ dir/sub/d.nsis dir/sub/e.TXT dir/sub/y.nsis")]
    [InlineData("+*.txt", "a.txt")]
    [InlineData("+*.txt|r", "a.txt dir/c.txt dir/sub/e.TXT Other/f.txt")]
    [InlineData("-*.nsis|r", "a.txt dir/ dir/c.txt dir/sub/ dir/sub/e.TXT emptydir/ Other/ Other/dir/ Other/f.txt")]
    [InlineData("-*.nsis", "a.txt dir/ dir/c.txt dir/sub/ dir/sub/d.nsis dir/sub/e.TXT dir/sub/y.nsis emptydir/ Other/ Other/dir/ Other/dir/x.nsis Other/f.txt")]
    [InlineData("+dir\\*|r", "dir/c.txt dir/sub/ dir/sub/d.nsis dir/sub/e.TXT dir/sub/y.nsis")]
    [InlineData("-dir\\*.nsis|r", "a.txt b.nsis dir/ dir/c.txt dir/sub/ dir/sub/e.TXT emptydir/ Other/ Other/dir/ Other/dir/x.nsis Other/f.txt")]
    [InlineData("+dir;-dir\\sub", "dir/ dir/c.txt")]
    [InlineData("-dir", "a.txt b.nsis emptydir/ Other/ Other/dir/ Other/dir/x.nsis Other/f.txt")]
    [InlineData("+c.txt|r", "dir/c.txt")]
    [InlineData("+c.txt|r0", "")]
    [InlineData("+*.txt|r0", "a.txt dir/c.txt dir/sub/e.TXT Other/f.txt")]
    [InlineData("+*", All)]
    [InlineData("", All)]
    [InlineData("+a.txt\\", "")]
    [InlineData("+?.txt", "a.txt")]
    [InlineData("+dir\\*", "dir/c.txt dir/sub/ dir/sub/d.nsis dir/sub/e.TXT dir/sub/y.nsis")]
    [InlineData("+sub|r", "dir/sub/ dir/sub/d.nsis dir/sub/e.TXT dir/sub/y.nsis")]
    [InlineData("+sub\\|r", "dir/sub/ dir/sub/d.nsis dir/sub/e.TXT dir/sub/y.nsis")]
    [InlineData("+Other\\dir", "Other/dir/ Other/dir/x.nsis")]
    [InlineData("+dir\\*.nsis|r", "dir/sub/d.nsis dir/sub/y.nsis")]
    [InlineData("+C:\\x\\a.txt", "")]
    [InlineData("+..\\a.txt", "")]
    [InlineData("+dir\\..\\a.txt", "")]
    [InlineData("+nothing.zzz", "")]
    public void Includes_TakesWhat7ZipTakes(string masks, string expected)
    {
        var filter = CliNameFilter.Create(ParseMasks(masks));

        Included(filter).Should().Equal(Split(expected));
    }

    [Fact]
    public void Includes_ExcludeFolders_TakesOnlyFiles()
    {
        // -xtd
        Included(CliNameFilter.Create([], excludeFolders: true)).Should().Equal(Split(
            "a.txt b.nsis dir/c.txt dir/sub/d.nsis dir/sub/e.TXT dir/sub/y.nsis Other/dir/x.nsis Other/f.txt"));
    }

    [Fact]
    public void Includes_ExcludeFiles_TakesOnlyFolders()
    {
        // -xtf
        Included(CliNameFilter.Create([], excludeFiles: true)).Should().Equal(Split("dir/ dir/sub/ emptydir/ Other/ Other/dir/"));
    }

    [Fact]
    public void Includes_WithoutWildcards_StarIsALiteralName()
    {
        // -spd *.txt
        Included(CliNameFilter.Create([new CliNameMask("*.txt", true, CliRecursion.None, WildcardMatching: false)]))
            .Should().BeEmpty();
    }

    [Fact]
    public void Includes_CaseSensitive_DoesNotTakeAnotherCase()
    {
        // -ssc DIR
        Included(CliNameFilter.Create([new CliNameMask("DIR", true, CliRecursion.None)], caseSensitive: true))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("*", "", true)]
    [InlineData("?", "", false)]
    [InlineData("**", "abc", true)]
    [InlineData("a?", "a", false)]
    [InlineData("a*c", "abbbc", true)]
    [InlineData("a*c", "abbbd", false)]
    [InlineData("*.TXT", "e.txt", true)]
    [InlineData("ж*", "Ж.txt", true)]
    [InlineData("", "", true)]
    [InlineData("", "a", false)]
    public void MaskMatches_StarAndQuestionMark(string mask, string name, bool expected)
    {
        CliNameFilter.MaskMatches(mask, name, caseSensitive: false).Should().Be(expected);
    }

    [Fact]
    public void MaskMatches_ManyStarsOnALongName_FinishesQuickly()
    {
        // A recursive matcher backtracks exponentially on this; entry names come from the archive.
        string mask = string.Concat(Enumerable.Repeat("*a", 40)) + "b";
        string name = new('a', 4000);
        var clock = Stopwatch.StartNew();

        CliNameFilter.MaskMatches(mask, name, caseSensitive: false).Should().BeFalse();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    private static List<string> Included(CliNameFilter filter) =>
        [.. Entries.Where(e => filter.Includes(e.Path, e.IsFolder)).Select(e => e.Path)];

    private static string[] Split(string list) => list.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<CliNameMask> ParseMasks(string masks) =>
        masks.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(m =>
        {
            string[] parts = m[1..].Split('|');
            CliRecursion recursion = parts.Length == 1 ? CliRecursion.None
                : parts[1] == "r" ? CliRecursion.Recursive : CliRecursion.WildcardOnly;
            return new CliNameMask(parts[0], m[0] == '+', recursion);
        });
}
