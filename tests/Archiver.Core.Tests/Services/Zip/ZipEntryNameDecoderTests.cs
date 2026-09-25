using System.Text;
using Archiver.Core.Services.Zip;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Zip;

// T-F234: 7-Zip's name rule (NanaZip ZipItem.cpp GetUnicodeString / ZipItem.h GetCodePage) with
// explicit code pages, so every expectation holds on any machine regardless of its OEM/ANSI pages.
public sealed class ZipEntryNameDecoderTests
{
    private const ushort Utf8Flag = 0x0800;
    private static readonly ZipNameCodePages Ru = ZipNameCodePages.FromCodePages(866, 1251);
    private static readonly ZipNameCodePages Us = ZipNameCodePages.FromCodePages(437, 1252);

    private static byte[] Cp(int codePage, string text) =>
        CodePagesEncodingProvider.Instance.GetEncoding(codePage)!.GetBytes(text);

    private static string Decode(byte[] raw, ushort flags, byte host, ZipNameCodePages pages, byte[]? extra = null) =>
        ZipEntryNameDecoder.Decode(raw, flags, host, extra ?? [], pages);

    // --- Happy path ---

    [Fact]
    public void Utf8Flag_DecodesUtf8_WhateverTheHost()
    {
        byte[] raw = Encoding.UTF8.GetBytes("Документ_\U0001F600.txt");

        Decode(raw, Utf8Flag, LegacyZipBuilder.HostFat, Us).Should().Be("Документ_\U0001F600.txt");
    }

    [Theory]
    [InlineData(LegacyZipBuilder.HostFat)]
    [InlineData(LegacyZipBuilder.HostNtfs)]
    public void NoFlag_FatOrNtfsHost_DecodesOem(byte host)
    {
        byte[] raw = Cp(866, "Тека/Документ_квартал.txt");

        Decode(raw, 0, host, Ru).Should().Be("Тека/Документ_квартал.txt");
    }

    [Fact]
    public void NoFlag_FatHost_UsOem_DecodesCp437()
    {
        Decode([0x80, 0x2E, 0x74, 0x78, 0x74], 0, LegacyZipBuilder.HostFat, Us).Should().Be("Ç.txt");
    }

    [Fact]
    public void NoFlag_OtherHost_DecodesAnsi()
    {
        byte[] raw = Cp(1251, "Звіт.txt");

        Decode(raw, 0, LegacyZipBuilder.HostMacOs, Ru).Should().Be("Звіт.txt");
    }

    [Fact]
    public void NoFlag_UnixHost_DecodesUtf8()
    {
        byte[] raw = Encoding.UTF8.GetBytes("звіт.txt");

        Decode(raw, 0, LegacyZipBuilder.HostUnix, Us).Should().Be("звіт.txt");
    }

    [Fact]
    public void ValidUnicodePathExtra_WinsOverCodePage()
    {
        byte[] raw = Cp(866, "А.txt");
        byte[] extra = LegacyZipBuilder.UnicodePathExtra(raw, Encoding.UTF8.GetBytes("Альфа.txt"));

        Decode(raw, 0, LegacyZipBuilder.HostFat, Us, extra).Should().Be("Альфа.txt");
    }

    [Fact]
    public void AsciiName_IsTheSameUnderEveryRule()
    {
        byte[] raw = Encoding.ASCII.GetBytes("docs/readme.txt");

        Decode(raw, 0, LegacyZipBuilder.HostFat, Ru).Should().Be("docs/readme.txt");
        Decode(raw, 0, LegacyZipBuilder.HostMacOs, Us).Should().Be("docs/readme.txt");
        Decode(raw, Utf8Flag, LegacyZipBuilder.HostUnix, Ru).Should().Be("docs/readme.txt");
    }

    // --- Security & Boundary ---

    [Fact]
    public void Backslash_IsNormalizedToSlash()
    {
        Decode(Encoding.ASCII.GetBytes(@"root\sub\a.txt"), 0, LegacyZipBuilder.HostFat, Us)
            .Should().Be("root/sub/a.txt");
    }

    [Fact]
    public void BackslashTraversal_IsNormalizedSoTheUnsafePathCheckSeesIt()
    {
        Decode(Encoding.ASCII.GetBytes(@"..\..\evil.txt"), 0, LegacyZipBuilder.HostFat, Us)
            .Should().Be("../../evil.txt");
    }

    [Fact]
    public void DbcsTrailByte0x5C_IsNotTreatedAsSeparator()
    {
        // Shift-JIS "ソ" is 0x83 0x5C — normalizing raw bytes would split it into "?" + "/".
        byte[] raw = [0x83, 0x5C, 0x2E, 0x74, 0x78, 0x74];

        Decode(raw, 0, LegacyZipBuilder.HostMacOs, ZipNameCodePages.FromCodePages(932, 932))
            .Should().Be("ソ.txt");
    }

    [Fact]
    public void UnicodePathExtra_WithWrongCrc_IsIgnored()
    {
        byte[] raw = Cp(866, "А.txt");
        byte[] extra = LegacyZipBuilder.UnicodePathExtra(raw, Encoding.UTF8.GetBytes("Альфа.txt"), crcOverride: 0xDEADBEEF);

        Decode(raw, 0, LegacyZipBuilder.HostFat, Ru, extra).Should().Be("А.txt");
    }

    [Fact]
    public void UnicodePathExtra_WithUnknownVersion_IsIgnored()
    {
        byte[] raw = Cp(866, "А.txt");
        byte[] extra = LegacyZipBuilder.UnicodePathExtra(raw, Encoding.UTF8.GetBytes("Альфа.txt"), version: 2);

        Decode(raw, 0, LegacyZipBuilder.HostFat, Ru, extra).Should().Be("А.txt");
    }

    [Fact]
    public void UnicodePathExtra_WithInvalidUtf8_IsIgnored()
    {
        byte[] raw = Cp(866, "А.txt");
        byte[] extra = LegacyZipBuilder.UnicodePathExtra(raw, [0xC3, 0x28, 0x2E, 0x74]);

        Decode(raw, 0, LegacyZipBuilder.HostFat, Ru, extra).Should().Be("А.txt");
    }

    [Fact]
    public void UnicodePathExtra_WithEmbeddedNul_IsIgnored()
    {
        byte[] raw = Cp(866, "А.txt");
        byte[] extra = LegacyZipBuilder.UnicodePathExtra(raw, [0x61, 0x00, 0x62]);

        Decode(raw, 0, LegacyZipBuilder.HostFat, Ru, extra).Should().Be("А.txt");
    }

    // --- Misuse ---

    [Fact]
    public void Utf8Flag_WithInvalidUtf8_DecodesToReplacementCharacter_DoesNotThrow()
    {
        Decode([0x80, 0x2E, 0x74, 0x78, 0x74], Utf8Flag, LegacyZipBuilder.HostFat, Ru).Should().Be("�.txt");
    }

    // --- Error path ---

    [Fact]
    public void MalformedExtraBlock_IsIgnored_HostRuleApplies()
    {
        byte[] raw = Cp(866, "А.txt");
        // Declares a 0x7075 record of 200 bytes in a 9-byte block.
        byte[] extra = [0x75, 0x70, 0xC8, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00];

        Decode(raw, 0, LegacyZipBuilder.HostFat, Ru, extra).Should().Be("А.txt");
    }

    [Fact]
    public void TruncatedUnicodePathRecord_IsIgnored()
    {
        byte[] raw = Cp(866, "А.txt");
        byte[] extra = [0x75, 0x70, 0x02, 0x00, 0x01, 0x00];

        Decode(raw, 0, LegacyZipBuilder.HostFat, Ru, extra).Should().Be("А.txt");
    }
}
