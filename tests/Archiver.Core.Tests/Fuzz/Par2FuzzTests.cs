using System.Buffers.Binary;
using Archiver.Core.Recovery;
using Archiver.Core.Tests.Helpers;
using Archiver.Core.Tests.Recovery;
using FluentAssertions;

namespace Archiver.Core.Tests.Fuzz;

/// <summary>
/// T-F275: mutational fuzzing of the PAR2 reader, verifier and repairer. Two kinds of input: raw
/// mutations of a whole set file, which mostly die at the packet MD5, and mutations of one packet
/// body with its MD5 recomputed, which reach the field checks behind it. Either way nothing may
/// throw, and nothing may be written but the one output file a repair is given.
/// </summary>
[Trait("Category", "Fuzz")]
public sealed class Par2FuzzTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<string, int> Seeds() => new()
    {
        { "tiny.bin", 22 },
        { "data.bin", 300001 },
        { "архів.zip", 5000 },
    };

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task RawMutation_ReadVerifyRepair_NeverThrow(string name, int length)
    {
        byte[] seed = SeedSet(name);
        await FuzzRunner.RunAsync("par2-raw", name + ".par2", seed, input => RunAllAsync(name, length, input));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task PacketMutationWithValidHash_ReadVerifyRepair_NeverThrow(string name, int length)
    {
        byte[] seed = SeedSet(name);
        await FuzzRunner.RunAsync("par2-packet", name + ".par2", seed, input => RunAllAsync(name, length, RehashPackets(seed, input)));
    }

    private async Task RunAllAsync(string name, int length, byte[] set)
    {
        string folder = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string target = Par2TestData.WriteContent(folder, name, length);
        await using (FileStream stream = File.OpenWrite(target))
        {
            stream.Position = length / 2;
            stream.WriteByte(0x5A);
        }
        string par2 = Path.Combine(folder, name + ".par2");
        await File.WriteAllBytesAsync(par2, set);
        string output = Path.Combine(folder, "out.bin");

        Par2ReadResult read = Par2PacketReader.Read([par2], CancellationToken.None);
        if (Par2SetLocator.Select(read.Sets, target) is { } match)
        {
            Par2Verifier.Verify(target, match.Set, null, CancellationToken.None);
            Par2RepairResult repair = Par2Repairer.Repair(target, match.Set, output, null, CancellationToken.None);
            if (repair.Status == Par2RepairStatus.Repaired)
                new FileInfo(output).Length.Should().Be(match.Set.FileLength);
        }

        Directory.GetFiles(folder).Select(Path.GetFileName).Should().BeSubsetOf([name, name + ".par2", "out.bin"]);
        Directory.Delete(folder, recursive: true);
    }

    // The golden index and volume as one file, so every packet type is in one seed.
    private static byte[] SeedSet(string name)
    {
        string golden = Path.Combine(Par2TestData.GoldenDir, name);
        string volume = Directory.GetFiles(Par2TestData.GoldenDir, name + ".vol*.par2").Single();
        return [.. File.ReadAllBytes(golden + ".par2"), .. File.ReadAllBytes(volume)];
    }

    // Where the mutated input still holds the seed's packet boundaries, recompute each packet's
    // MD5 over its (possibly mutated) bytes, so field values get past the hash check.
    private static byte[] RehashPackets(byte[] seed, byte[] mutated)
    {
        byte[] result = (byte[])mutated.Clone();
        for (int offset = 0; offset + 64 <= seed.Length;)
        {
            int length = (int)BinaryPrimitives.ReadInt64LittleEndian(seed.AsSpan(offset + 8));
            ulong declared = offset + 16 <= result.Length ? BinaryPrimitives.ReadUInt64LittleEndian(result.AsSpan(offset + 8)) : 0;
            if (declared >= 64 && declared <= (ulong)(result.Length - offset))
                Par2PacketForge.Md5(result.AsSpan(offset + 32, (int)declared - 32)).CopyTo(result, offset + 16);
            offset += length;
        }
        return result;
    }
}
