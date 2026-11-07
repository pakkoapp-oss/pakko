using Archiver.Core.Text;
using FluentAssertions;

namespace Archiver.Core.Tests.Text;

// T-F467: every handler of the one provider, through explicit machine values, so the matrix runs on
// any machine: equal pages, a format whose page differs from the system's, UTF-8, and a DBCS page.
public sealed class TextBoundariesTests
{
    public static TheoryData<int, int, string?> Machines => new()
    {
        { 1252, 437, "English_United States" },
        { 1252, 437, "Ukrainian_Ukraine" },
        { 1251, 866, "English_United States" },
        { 65001, 437, "English_United States" },
        { 932, 932, "Japanese_Japan" },
        { 1252, 437, null },
    };

    // T-F465: tar.exe's output and its command line share one page, whatever the format.
    [Theory]
    [MemberData(nameof(Machines))]
    public void TarExe_OutputMessagesAndCommandLine_ShareTheSystemAnsiPage(int ansi, int oem, string? ucrt)
    {
        var machine = new MachinePages(ansi, oem, ucrt);

        TextBoundaries.TarExeOutput(machine).CodePage.Should().Be(ansi);
        TextBoundaries.TarExeCommandLine(machine).CodePage.Should().Be(ansi);
        TextBoundaries.TarExeMessages(machine).CodePage.Should().Be(ansi);
    }

    [Fact]
    public void TarExeOutput_Utf8System_ReadsUtf8WithoutPreamble()
    {
        TextBoundary output = TextBoundaries.TarExeOutput(new MachinePages(65001, 437, "English_United States"));

        output.Lenient.GetString([0xD0, 0x96]).Should().Be(((char)0x416).ToString());
        output.Lenient.GetPreamble().Should().BeEmpty();
    }

    // Measured in Pakko's sandbox on 3.5.2 (system 1252, OEM 437): the page libarchive read raw
    // ustar bytes in, per format.
    [Theory]
    [InlineData("English_United States", 437)]
    [InlineData("English_United Kingdom", 850)]
    [InlineData("German_Germany", 850)]
    [InlineData("Portuguese_Brazil", 850)]
    [InlineData("Polish_Poland", 852)]
    [InlineData("Russian_Russia", 866)]
    [InlineData("Japanese_Japan", 932)]
    [InlineData("Korean_Korea", 949)]
    [InlineData("Ukrainian_Ukraine", 437)]
    [InlineData("Chinese (Simplified)_China", 437)]
    [InlineData("Chinese (Traditional)_Taiwan", 437)]
    [InlineData("Norwegian Bokm\u00E5l_Norway", 437)]
    public void TarRawHeaderNames_FollowLibarchivesTable(string ucrt, int expected)
    {
        TextBoundaries.TarRawHeaderNames(new MachinePages(1252, 437, ucrt)).CodePage.Should().Be(expected);
    }

    // libarchive compares strncmp(row, name, length of name): a name that begins a row takes it, the
    // first such row in table order; a name longer than every row takes none.
    [Fact]
    public void TarRawHeaderNames_ANameThatBeginsARow_TakesTheFirstSuchRow()
    {
        TextBoundaries.TarRawHeaderNames(new MachinePages(1252, 437, "English")).CodePage.Should().Be(437);
        TextBoundaries.TarRawHeaderNames(new MachinePages(1252, 437, "Chinese")).CodePage.Should().Be(950);
        TextBoundaries.TarRawHeaderNames(new MachinePages(1252, 437, "Korean_Korea Republic")).CodePage.Should().Be(437);
    }

    [Fact]
    public void TarRawHeaderNames_NoLocaleName_TheSystemOemPage()
    {
        TextBoundaries.TarRawHeaderNames(new MachinePages(1251, 866, null)).CodePage.Should().Be(866);
        TextBoundaries.TarRawHeaderNames(new MachinePages(1251, 866, "")).CodePage.Should().Be(866);
    }

    [Theory]
    [MemberData(nameof(Machines))]
    public void ZipLegacyNames_TheSystemPair(int ansi, int oem, string? ucrt)
    {
        (TextBoundary zipOem, TextBoundary zipAnsi) = TextBoundaries.ZipLegacyNames(new MachinePages(ansi, oem, ucrt));

        zipOem.CodePage.Should().Be(oem);
        zipAnsi.CodePage.Should().Be(ansi);
    }

    [Theory]
    [MemberData(nameof(Machines))]
    public void Console_Sentinels(int ansi, int oem, string? ucrt)
    {
        var machine = new MachinePages(ansi, oem, ucrt);

        TextBoundaries.Console(0, machine).CodePage.Should().Be(ansi);
        TextBoundaries.Console(1, machine).CodePage.Should().Be(oem);
        TextBoundaries.Console(65001, machine).CodePage.Should().Be(65001);
    }

    [Fact]
    public void FixedBoundaries_Utf8WithoutPreamble()
    {
        TextBoundaries.Par2FileNames.CodePage.Should().Be(65001);
        TextBoundaries.Par2FileNames.GetPreamble().Should().BeEmpty();
        TextBoundaries.LaunchArgumentPayload.CodePage.Should().Be(65001);
        TextBoundaries.LaunchArgumentPayload.GetPreamble().Should().BeEmpty();
    }

    [Fact]
    public void Strict_Throws_Lenient_Replaces()
    {
        var boundary = new TextBoundary(1251);
        string cafe = "caf" + (char)0xE9;

        boundary.Lenient.GetBytes(cafe).Should().HaveCount(4);
        FluentActions.Invoking(() => boundary.Strict.GetBytes(cafe)).Should().Throw<System.Text.EncoderFallbackException>();
    }

    [Fact]
    public void MachinePagesCurrent_ReadsThisMachine()
    {
        MachinePages machine = MachinePages.Current;

        machine.Ansi.Should().Be(SystemCodePages.Ansi);
        machine.Oem.Should().Be(SystemCodePages.Oem);
        machine.UcrtLocaleName.Should().NotBeNullOrEmpty().And.Contain("_");
    }
}
