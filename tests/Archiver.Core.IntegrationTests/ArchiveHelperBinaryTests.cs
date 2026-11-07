using System.Buffers.Binary;
using System.Text;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F470: the helper starts inside an AppContainer without a desktop only while kernel32.dll is
/// its one import (a user32 or ole32 import fails it with 0xC0000142, docs/DECISIONS.md "T-F468
/// research done"), and it hosts an untrusted-input parser, so its mitigations are pinned too.
/// </summary>
public sealed class ArchiveHelperBinaryTests
{
    private const ushort DynamicBase = 0x0040;
    private const ushort HighEntropyVa = 0x0020;
    private const ushort NxCompat = 0x0100;
    private const ushort GuardCf = 0x4000;
    private static readonly ushort[] Required = [DynamicBase, HighEntropyVa, NxCompat, GuardCf];

    [Fact]
    public void Imports_OnlyKernel32()
    {
        Pe pe = Pe.Read(ArchiveHelperBuild.ExePath);

        pe.ImportedDlls.Should().Equal(["KERNEL32.dll"], "an AppContainer child importing anything else fails to start without a desktop");
        pe.HasDelayImports.Should().BeFalse("a delay-loaded user32/ole32 would fail at first call instead of at start");
    }

    [Fact]
    public void Mitigations_AreOn()
    {
        Pe pe = Pe.Read(ArchiveHelperBuild.ExePath);

        pe.Machine.Should().Be(0x8664);
        foreach (ushort flag in Required)
            (pe.DllCharacteristics & flag).Should().Be(flag, $"DLL characteristic 0x{flag:X4} must be set");
    }

    private sealed record Pe(ushort Machine, ushort DllCharacteristics, IReadOnlyList<string> ImportedDlls, bool HasDelayImports)
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
            return new Pe(machine, characteristics, dlls, delayRva != 0);
        }
    }
}
