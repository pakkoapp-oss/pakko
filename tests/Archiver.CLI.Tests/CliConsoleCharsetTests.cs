using System.Text;
using Archiver.CLI;
using FluentAssertions;

namespace Archiver.CLI.Tests;

public sealed class CliConsoleCharsetTests
{
    [Theory]
    [InlineData(CliConsoleCharset.Utf8)]
    [InlineData(CliConsoleCharset.Ansi)]
    [InlineData(CliConsoleCharset.Oem)]
    public void CreateEncoding_NeverHasPreamble(int charset)
    {
        Encoding encoding = CliConsoleCharset.CreateEncoding(charset);

        encoding.GetPreamble().Should().BeEmpty();
    }

    [Fact]
    public void CreateEncoding_Utf8_RoundTripsCyrillicAndCjk()
    {
        Encoding encoding = CliConsoleCharset.CreateEncoding(CliConsoleCharset.Utf8);

        encoding.GetString(encoding.GetBytes("Звіт 报告")).Should().Be("Звіт 报告");
    }

    [Fact]
    public void Apply_WritesThroughStreamWriterWithoutBom()
    {
        using var buffer = new MemoryStream();
        using (var writer = new StreamWriter(buffer, CliConsoleCharset.CreateEncoding(CliConsoleCharset.Utf8), leaveOpen: true))
            writer.Write("ї");

        buffer.ToArray().Should().Equal(0xD1, 0x97);
    }
}
