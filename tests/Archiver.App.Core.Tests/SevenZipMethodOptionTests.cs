using Archiver.Core.Models;
using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F342: the New archive card's method list for 7z.
public sealed class SevenZipMethodOptionTests
{
    [Fact]
    public void Names_AreTheFiveMethods_Lzma2First() =>
        SevenZipMethodOption.Names.Should().Equal("LZMA2", "LZMA", "BZip2", "Deflate", "Copy");

    [Fact]
    public void EveryMethod_HasARowAndComesBackFromIt()
    {
        foreach (SevenZipMethod method in Enum.GetValues<SevenZipMethod>())
            SevenZipMethodOption.FromIndex(SevenZipMethodOption.ToIndex(method)).Should().Be(method);

        Enum.GetValues<SevenZipMethod>().Select(SevenZipMethodOption.ToIndex)
            .Should().BeEquivalentTo(Enumerable.Range(0, SevenZipMethodOption.Names.Count));
    }

    [Theory]
    [InlineData(SevenZipMethod.Lzma2, "LZMA2")]
    [InlineData(SevenZipMethod.BZip2, "BZip2")]
    [InlineData(SevenZipMethod.Copy, "Copy")]
    public void Name_IsTheRowsText(SevenZipMethod method, string expected) =>
        SevenZipMethodOption.Name(method).Should().Be(expected);

    // A ComboBox reports -1 while it has no selection.
    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void FromIndex_OutOfRange_IsTheDefaultMethod(int index) =>
        SevenZipMethodOption.FromIndex(index).Should().Be(SevenZipMethod.Lzma2);

    [Fact]
    public void ToIndex_UnknownMethod_IsTheDefaultRow() =>
        SevenZipMethodOption.ToIndex((SevenZipMethod)99).Should().Be(0);

    [Theory]
    [InlineData(ArchiveContainerFormat.SevenZip, SevenZipMethod.Lzma2, true)]
    [InlineData(ArchiveContainerFormat.SevenZip, SevenZipMethod.Copy, false)]
    [InlineData(ArchiveContainerFormat.Tar, SevenZipMethod.Lzma2, false)]
    [InlineData(ArchiveContainerFormat.Zip, SevenZipMethod.Copy, true)]
    [InlineData(ArchiveContainerFormat.TarGz, SevenZipMethod.Copy, true)]
    public void HasCompressionLevel_NotForPlainTarOrACopied7z(ArchiveContainerFormat format, SevenZipMethod method, bool expected) =>
        SevenZipMethodOption.HasCompressionLevel(format, method).Should().Be(expected);

    // T-F403: the list holds only the methods this tar.exe can write; rows are positions in it.
    private static readonly SevenZipMethod[] NoBZip2 = [SevenZipMethod.Lzma2, SevenZipMethod.Lzma, SevenZipMethod.Deflate, SevenZipMethod.Copy];

    [Fact]
    public void Rows_OfAShorterList_MapToItsMethods()
    {
        SevenZipMethodOption.FromIndex(2, NoBZip2).Should().Be(SevenZipMethod.Deflate);
        SevenZipMethodOption.ToIndex(SevenZipMethod.Copy, NoBZip2).Should().Be(3);
        SevenZipMethodOption.NamesOf(NoBZip2).Should().Equal("LZMA2", "LZMA", "Deflate", "Copy");
    }

    [Fact]
    public void MethodNotInTheList_IsTheFirstRow_AndAnOutOfRangeRowIsTheFirstMethod()
    {
        SevenZipMethodOption.ToIndex(SevenZipMethod.BZip2, NoBZip2).Should().Be(0);
        SevenZipMethodOption.FromIndex(-1, NoBZip2).Should().Be(SevenZipMethod.Lzma2);
        SevenZipMethodOption.FromIndex(0, []).Should().Be(SevenZipMethod.Lzma2);
    }
}
