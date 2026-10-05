using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Zip.Decryption;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip.Decryption;

public sealed class EntryRegionStreamTests
{
    [Fact]
    public void Read_RegionInsideTheSource_ReturnsItsBytes()
    {
        using var region = new EntryRegionStream(new MemoryStream([1, 2, 3, 4, 5]), start: 1, length: 3, ownsSource: true);
        using var copy = new MemoryStream();
        region.CopyTo(copy);
        copy.ToArray().Should().Equal(2, 3, 4);
    }

    // T-F333: a truncated archive's error carries a code, so Shell and App do not show bare English.
    [Fact]
    public void Read_SourceEndsBeforeTheRegion_ErrorCarriesItsCode()
    {
        using var region = new EntryRegionStream(new MemoryStream([1, 2, 3]), start: 1, length: 10, ownsSource: true);
        Action act = () => region.CopyTo(Stream.Null);

        EndOfStreamException ex = act.Should().Throw<EndOfStreamException>().Which;
        CoreText detail = CoreMessages.Detail(ex);
        detail.Code.Should().Be(MessageCode.EntryDataTruncated);
        ex.Message.Should().Be("ZIP entry data ends before its declared size.");
    }
}
