using System.Buffers.Binary;
using System.Text;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F470: the helper starts inside an AppContainer without a desktop only while kernel32.dll is
/// its one import (a user32 or ole32 import fails it with 0xC0000142, docs/DECISIONS.md "T-F468
/// research done"), and it hosts an untrusted-input parser, so its mitigations are pinned too.
/// </summary>
[Collection("TarSandbox")]
public sealed class ArchiveHelperBinaryTests
{
    private const ushort DynamicBase = 0x0040;
    private const ushort HighEntropyVa = 0x0020;
    private const ushort NxCompat = 0x0100;
    private const ushort GuardCf = 0x4000;
    private static readonly ushort[] Required = [DynamicBase, HighEntropyVa, NxCompat, GuardCf];
    private const uint CetCompat = 0x0001;

    [Theory]
    [InlineData("x64")]
    [InlineData("ARM64")]
    public void Imports_OnlyKernel32(string platform)
    {
        var pe = Pe.Read(ArchiveHelperBuild.ExePathFor(platform));

        pe.ImportedDlls.Should().Equal(["KERNEL32.dll"], "an AppContainer child importing anything else fails to start without a desktop");
        pe.HasDelayImports.Should().BeFalse("a delay-loaded user32/ole32 would fail at first call instead of at start");
    }

    [Theory]
    [InlineData("x64", 0x8664)]
    [InlineData("ARM64", 0xAA64)]
    public void Mitigations_AreOn(string platform, int machine)
    {
        var pe = Pe.Read(ArchiveHelperBuild.ExePathFor(platform));

        pe.Machine.Should().Be((ushort)machine);
        foreach (ushort flag in Required)
            (pe.DllCharacteristics & flag).Should().Be(flag, $"DLL characteristic 0x{flag:X4} must be set");
        if (platform == "x64")
            (pe.ExtendedDllCharacteristics & CetCompat).Should().Be(CetCompat, "x64 is linked /CETCOMPAT");
    }

    // T-F377: the certification kit flags a packaged exe that declares no DPI awareness.
    [Theory]
    [InlineData("x64")]
    [InlineData("ARM64")]
    public void Manifest_DeclaresPerMonitorV2(string platform)
    {
        byte[] exe = File.ReadAllBytes(ArchiveHelperBuild.ExePathFor(platform));

        Encoding.ASCII.GetString(exe).Should().Contain(">PerMonitorV2</dpiAwareness>");
    }

    private sealed record Pe(ushort Machine, ushort DllCharacteristics, uint ExtendedDllCharacteristics, IReadOnlyList<string> ImportedDlls,
        bool HasDelayImports)
    {
        public static Pe Read(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            int pe = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x3C));
            Encoding.ASCII.GetString(b, pe, 4).Should().Be("PE\0\0");
            ushort machine = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pe + 4));
            ushort sections = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pe + 6));
            ushort optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pe + 20));
            int optional = pe + 24;
            BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(optional)).Should().Be(0x20B, "PE32+");
            ushort characteristics = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(optional + 70));
            int directories = optional + 112;
            uint importRva = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(directories + 8));
            uint debugRva = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(directories + 6 * 8));
            uint debugSize = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(directories + 6 * 8 + 4));
            uint delayRva = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(directories + 13 * 8));
            int sectionTable = optional + optionalSize;

            int Offset(uint rva)
            {
                for (int i = 0; i < sections; i++)
                {
                    int s = sectionTable + i * 40;
                    uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(s + 8));
                    uint virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(s + 12));
                    uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(s + 16));
                    uint rawPointer = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(s + 20));
                    if (rva >= virtualAddress && rva < virtualAddress + Math.Max(virtualSize, rawSize))
                        return checked((int)(rva - virtualAddress + rawPointer));
                }
                throw new InvalidDataException($"RVA 0x{rva:X} is in no section");
            }

            var dlls = new List<string>();
            for (int d = Offset(importRva); ; d += 20)
            {
                uint nameRva = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(d + 12));
                if (nameRva == 0)
                    break;
                int n = Offset(nameRva);
                dlls.Add(Encoding.ASCII.GetString(b, n, Array.IndexOf(b, (byte)0, n) - n));
            }
            // IMAGE_DEBUG_TYPE_EX_DLLCHARACTERISTICS (20) carries the CET bit.
            uint extended = 0;
            if (debugRva != 0)
            {
                for (int d = Offset(debugRva), stop = d + (int)debugSize; d + 28 <= stop; d += 28)
                {
                    if (BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(d + 12)) == 20)
                        extended = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(d + 24))));
                }
            }
            return new Pe(machine, characteristics, extended, dlls, delayRva != 0);
        }
    }
}
