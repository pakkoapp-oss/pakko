using System.Text;
using Archiver.Core.IO;
using FluentAssertions;

namespace Archiver.Core.Tests.IO;

public sealed class VerifyingReadStreamTests
{
    private static readonly byte[] Data = Encoding.ASCII.GetBytes("verifying-read-stream content");
    private static readonly uint DataCrc = Crc32.Compute(new MemoryStream(Data));

    [Fact]
    public void Read_ExactDeclaredLengthAndMatchingCrc_ReturnsContent()
    {
        using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length, DataCrc);
        using var copy = new MemoryStream();
        s.CopyTo(copy);
        copy.ToArray().Should().Equal(Data);
    }

    [Fact]
    public async Task ReadAsync_ExactDeclaredLengthAndMatchingCrc_ReturnsContent()
    {
        await using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length, DataCrc);
        using var copy = new MemoryStream();
        await s.CopyToAsync(copy);
        copy.ToArray().Should().Equal(Data);
    }

    [Fact]
    public void Read_ContentLongerThanDeclared_Throws()
    {
        using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length - 1, expectedCrc32: null);
        var act = () => s.CopyTo(Stream.Null);
        act.Should().Throw<InvalidDataException>().WithMessage("*declared size*");
    }

    [Fact]
    public async Task ReadAsync_WrongCrc_ThrowsAtEndOfStream()
    {
        await using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length, DataCrc ^ 1);
        var act = () => s.CopyToAsync(Stream.Null);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*CRC-32*");
    }

    [Fact]
    public void Read_EmptyContentWithZeroCrc_Succeeds()
    {
        using var s = new VerifyingReadStream(new MemoryStream(), 0, 0u);
        var act = () => s.CopyTo(Stream.Null);
        act.Should().NotThrow();
    }

    [Fact]
    public void Read_NoExpectedCrc_ShorterContentIsAccepted()
    {
        using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length + 100, expectedCrc32: null);
        var act = () => s.CopyTo(Stream.Null);
        act.Should().NotThrow();
    }
}
