using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F220 item 5: a nested archive is browsed from a temp copy; the status line showed that copy's
// path (%TEMP%\PakkoNestedArchive\<guid>\l4.zip) instead of where the user is.
public sealed class NestedDisplayPathTests
{
    private const string Temp = @"C:\Users\u\AppData\Local\Temp\PakkoNestedArchive\0f3a\l4.zip";

    [Fact]
    public void Map_ReplacesTheTempCopyWithTheChain()
    {
        string chain = NestedDisplayPath.Chain(["outer.zip", "docs", "l2.zip", "l4.zip"]);

        NestedDisplayPath.Map(Temp + @"\a.txt", Temp, chain).Should().Be(@"outer.zip > docs > l2.zip > l4.zip\a.txt");
        NestedDisplayPath.Map(Temp, Temp, chain).Should().Be("outer.zip > docs > l2.zip > l4.zip");
    }

    [Fact]
    public void Map_IgnoresCase_AndLeavesOtherTextAlone()
    {
        NestedDisplayPath.Map(Temp.ToUpperInvariant(), Temp, "x > y").Should().Be("x > y");
        NestedDisplayPath.Map("entry.txt", Temp, "x > y").Should().Be("entry.txt");
    }

    [Fact]
    public void Map_NotNested_KeepsTheText() =>
        NestedDisplayPath.Map(@"C:\real\a.zip", null, "x").Should().Be(@"C:\real\a.zip");
}
