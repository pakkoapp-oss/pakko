using System.Globalization;
using System.Text;
using Archiver.Core.IO;
using Archiver.Core.Models;
using Archiver.Core.Services;
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
        Action act = () => s.CopyTo(Stream.Null);
        act.Should().Throw<InvalidDataException>().WithMessage("*declared size*");
    }

    [Fact]
    public async Task ReadAsync_WrongCrc_ThrowsAtEndOfStream()
    {
        await using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length, DataCrc ^ 1);
        Func<Task> act = () => s.CopyToAsync(Stream.Null);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*CRC-32*");
    }

    // T-F297: Core's own text carries its code, so a frontend can add a translation.
    [Fact]
    public void Read_WrongCrc_ErrorCarriesItsCode()
    {
        using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length, DataCrc ^ 1);
        Action act = () => s.CopyTo(Stream.Null);

        InvalidDataException ex = act.Should().Throw<InvalidDataException>().Which;
        CoreText detail = CoreMessages.Detail(ex);
        detail.Code.Should().Be(MessageCode.ContentCrcMismatch);
        detail.English.Should().Be($"Content failed CRC-32 check (expected {DataCrc ^ 1:X8}, got {DataCrc:X8}).");
    }

    // T-F333: the two size errors carry a code too, so Shell and App do not show bare English.
    [Theory]
    [InlineData(-1, MessageCode.ContentLargerThanDeclared, "Content is larger than its declared size ({0} bytes).")]
    [InlineData(1, MessageCode.ContentSmallerThanDeclared, "Content is smaller than its declared size ({0} bytes).")]
    public void Read_ContentNotOfDeclaredSize_ErrorCarriesItsCode(int declaredDelta, MessageCode expectedCode, string expectedEnglish)
    {
        long declared = Data.Length + declaredDelta;
        using var s = new VerifyingReadStream(new MemoryStream(Data), declared, expectedCrc32: null);
        Action act = () => s.CopyTo(Stream.Null);

        InvalidDataException ex = act.Should().Throw<InvalidDataException>().Which;
        CoreText detail = CoreMessages.Detail(ex);
        detail.Code.Should().Be(expectedCode);
        detail.English.Should().Be(string.Format(CultureInfo.InvariantCulture, expectedEnglish, declared));
        ex.Message.Should().Be(detail.English);
    }

    [Fact]
    public void Read_EmptyContentWithZeroCrc_Succeeds()
    {
        using var s = new VerifyingReadStream(new MemoryStream(), 0, 0u);
        Action act = () => s.CopyTo(Stream.Null);
        act.Should().NotThrow();
    }

    [Fact]
    public void Read_ContentShorterThanDeclared_ThrowsAtEndOfStream()
    {
        using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length + 100, expectedCrc32: null);
        Action act = () => s.CopyTo(Stream.Null);
        act.Should().Throw<InvalidDataException>().WithMessage("*smaller than its declared size*");
    }

    [Fact]
    public async Task ReadAsync_ContentShorterThanDeclared_ThrowsAtEndOfStream()
    {
        await using var s = new VerifyingReadStream(new MemoryStream(Data), Data.Length + 1, DataCrc);
        Func<Task> act = () => s.CopyToAsync(Stream.Null);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*smaller than its declared size*");
    }
}
