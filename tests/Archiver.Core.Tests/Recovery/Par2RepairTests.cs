using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Archiver.Core.Recovery;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Recovery;

// T-F275: verify and repair, end to end over real files: Pakko's own sets and par2cmdline's.
public sealed class Par2RepairTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // --- Happy path ---

    [Fact]
    public void Verify_IntactFile_IsIntact()
    {
        (string file, Par2Set set) = Protect("a.zip", 10000, 5);

        Par2Verification result = Par2Verifier.Verify(file, set, null, CancellationToken.None);

        result.Status.Should().Be(Par2VerifyStatus.Intact);
        result.DamagedSlices.Should().BeEmpty();
    }

    [Fact]
    public void Repair_IntactFile_WritesNothing()
    {
        (string file, Par2Set set) = Protect("a.zip", 10000, 5);
        string output = Output();

        Par2Repairer.Repair(file, set, output, null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.NothingToRepair);

        File.Exists(output).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4999)]
    [InlineData(9999)]
    public void Repair_OneFlippedByte_RestoresTheOriginal(int offset)
    {
        (string file, Par2Set set) = Protect("a.zip", 10000, 5);
        byte[] original = File.ReadAllBytes(file);
        Damage(file, offset, 1);
        byte[] damaged = File.ReadAllBytes(file);
        string output = Output();

        Par2RepairResult result = Par2Repairer.Repair(file, set, output, null, CancellationToken.None);

        result.Should().Be(new Par2RepairResult(Par2RepairStatus.Repaired, 1));
        File.ReadAllBytes(output).Should().Equal(original);
        File.ReadAllBytes(file).Should().Equal(damaged, "the damaged file is never written");
    }

    [Fact]
    public void Repair_AsManyDamagedSlicesAsRecoveryBlocks_RestoresTheOriginal()
    {
        (string file, Par2Set set) = Protect("a.zip", 2000, 10, sliceSize: 20);
        byte[] original = File.ReadAllBytes(file);
        foreach (int slice in new[] { 0, 7, 33, 50, 51, 52, 60, 77, 98, 99 })
            Damage(file, slice * 20 + 3, 1);

        Par2Repairer.Repair(file, set, Output(), null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.Repaired);

        File.ReadAllBytes(Output()).Should().Equal(original);
    }

    [Fact]
    public void Repair_ZeroedWholeSlices_RestoresTheOriginal()
    {
        (string file, Par2Set set) = Protect("a.zip", 300001, 10);
        byte[] original = File.ReadAllBytes(file);
        using (FileStream stream = File.OpenWrite(file))
        {
            stream.Position = 4096;
            stream.Write(new byte[8192]);
        }

        Par2Repairer.Repair(file, set, Output(), null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.Repaired);

        File.ReadAllBytes(Output()).Should().Equal(original);
    }

    [Fact]
    public void Repair_TruncatedTail_RestoresTheOriginal()
    {
        (string file, Par2Set set) = Protect("a.zip", 10000, 10);
        byte[] original = File.ReadAllBytes(file);
        using (FileStream stream = File.OpenWrite(file))
            stream.SetLength(9900);

        Par2Verifier.Verify(file, set, null, CancellationToken.None).Status.Should().Be(Par2VerifyStatus.Repairable);
        Par2Repairer.Repair(file, set, Output(), null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.Repaired);

        File.ReadAllBytes(Output()).Should().Equal(original);
    }

    [Fact]
    public void Repair_BytesAppended_CutsThemOff()
    {
        (string file, Par2Set set) = Protect("a.zip", 10000, 5);
        byte[] original = File.ReadAllBytes(file);
        File.AppendAllText(file, "trailing");

        Par2RepairResult result = Par2Repairer.Repair(file, set, Output(), null, CancellationToken.None);

        result.Should().Be(new Par2RepairResult(Par2RepairStatus.Repaired, 0));
        File.ReadAllBytes(Output()).Should().Equal(original);
    }

    [Fact]
    public void Repair_FileDeletedWithFullRedundancy_RebuildsItFromTheSetAlone()
    {
        (string file, Par2Set set) = Protect("a.zip", 5000, 100, sliceSize: 100);
        byte[] original = File.ReadAllBytes(file);
        File.Delete(file);

        Par2Verification verification = Par2Verifier.Verify(file, set, null, CancellationToken.None);
        verification.FileMissing.Should().BeTrue();
        Par2Repairer.Repair(file, set, Output(), null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.Repaired);

        File.ReadAllBytes(Output()).Should().Equal(original);
    }

    [Theory]
    [InlineData("tiny.bin", 22, 3)]
    [InlineData("data.bin", 300001, 4)]
    [InlineData("архів.zip", 5000, 2)]
    public void Repair_WithPar2cmdlineSet_RestoresTheOriginal(string name, int length, int recoveryCount)
    {
        string file = Par2TestData.WriteContent(_temp.Path, name, length);
        string golden = Path.Combine(Par2TestData.GoldenDir, name);
        Par2Set set = Par2PacketReader.Read([golden + ".par2", $"{golden}.vol0+{recoveryCount}.par2"], CancellationToken.None).Sets.Single();
        long sliceSize = set.SliceSize;
        for (int slice = 0; slice < recoveryCount; slice++)
            Damage(file, (int)Math.Min(length - 1, slice * 2 * sliceSize), 1);

        Par2Repairer.Repair(file, set, Output(), null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.Repaired);

        File.ReadAllBytes(Output()).Should().Equal(Par2TestData.Content(length));
    }

    [Fact]
    public void Repair_ReportsProgressUpToOne()
    {
        (string file, Par2Set set) = Protect("a.zip", 100000, 10);
        Damage(file, 50000, 100);
        var reports = new List<double>();

        Par2Repairer.Repair(file, set, Output(), reports.Add, CancellationToken.None);

        reports.Should().BeInAscendingOrder();
        reports[^1].Should().BeApproximately(1.0, 1e-9);
    }

    // Slices above the 16 MiB range width: the combination runs range by range.
    [Fact]
    [Trait("Category", "Slow")]
    public void Repair_SlicesLargerThanOneRange_RestoresTheOriginal()
    {
        const long sliceSize = 20L << 20;
        int length = (int)(2 * sliceSize + 1);
        (string file, Par2Set set, _) = ProtectWithVolume("a.zip", length, new Par2Parameters(sliceSize, 3, 2));
        byte[] original = File.ReadAllBytes(file);
        Damage(file, (int)sliceSize + (17 << 20), 1);
        Damage(file, length - 1, 1);

        Par2Repairer.Repair(file, set, Output(), null, CancellationToken.None).Should().Be(new Par2RepairResult(Par2RepairStatus.Repaired, 2));

        File.ReadAllBytes(Output()).Should().Equal(original);
    }

    // --- Error path ---

    [Fact]
    public void Repair_OneDamagedSliceMoreThanRecoveryBlocks_FailsCleanly()
    {
        (string file, Par2Set set) = Protect("a.zip", 2000, 5, sliceSize: 20);
        foreach (int slice in new[] { 1, 2, 3, 4, 5, 6 })
            Damage(file, slice * 20, 1);
        string output = Output();

        Par2Verifier.Verify(file, set, null, CancellationToken.None).Status.Should().Be(Par2VerifyStatus.NotRepairable);
        Par2Repairer.Repair(file, set, output, null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.NotRepairable);

        Directory.GetFiles(_temp.Path).Should().NotContain(output).And.NotContain(p => p.EndsWith(".tmp", StringComparison.Ordinal));
    }

    // The case par2cmdline 1.4.0 cannot repair (exit 6, "RS computation error"): slices
    // [2, 48, 237] missing, recovery blocks {1, 2, 4, 5} left — {1, 2, 4} are dependent there.
    [Fact]
    public void Repair_FirstBlocksDependent_SolvesWithTheSpareBlock()
    {
        (string file, Par2Set set, string volume) = ProtectWithVolume("a.zip", 960, new Par2Parameters(4, 240, 6));
        byte[] original = File.ReadAllBytes(file);
        DestroyRecoveryBlocks(volume, 0, 3);
        foreach (int slice in new[] { 2, 48, 237 })
            Damage(file, slice * 4, 4);
        set = ReadSet(file, volume);
        set.RecoveryBlocks.Select(b => b.Exponent).Should().Equal(1u, 2u, 4u, 5u);

        Par2Repairer.Repair(file, set, Output(), null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.Repaired);

        File.ReadAllBytes(Output()).Should().Equal(original);
    }

    [Fact]
    public void Repair_OnlyDependentBlocksLeft_FailsCleanly()
    {
        (string file, _, string volume) = ProtectWithVolume("a.zip", 960, new Par2Parameters(4, 240, 6));
        DestroyRecoveryBlocks(volume, 0, 3, 5);
        foreach (int slice in new[] { 2, 48, 237 })
            Damage(file, slice * 4, 4);
        Par2Set set = ReadSet(file, volume);
        string output = Output();

        Par2Verifier.Verify(file, set, null, CancellationToken.None).Status.Should().Be(Par2VerifyStatus.NotRepairable);
        Par2Repairer.Repair(file, set, output, null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.NotRepairable);

        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public void Repair_IndexDamaged_UsesTheCopiesInTheVolume()
    {
        (string file, _, string volume) = ProtectWithVolume("a.zip", 10000, Par2Creator.ChooseParameters(10000, 10)!.Value);
        byte[] original = File.ReadAllBytes(file);
        string index = Par2Creator.IndexPath(file);
        byte[] indexBytes = File.ReadAllBytes(index);
        Array.Fill(indexBytes, (byte)0xEE, 0, indexBytes.Length / 2);
        File.WriteAllBytes(index, indexBytes);
        Damage(file, 10, 1);

        Par2Repairer.Repair(file, ReadSet(file, index, volume), Output(), null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.Repaired);

        File.ReadAllBytes(Output()).Should().Equal(original);
    }

    [Fact]
    public void Repair_RecoveryBlockDamaged_UsesTheOthers()
    {
        (string file, _, string volume) = ProtectWithVolume("a.zip", 960, new Par2Parameters(4, 240, 3));
        byte[] original = File.ReadAllBytes(file);
        DestroyRecoveryBlocks(volume, 1);
        Damage(file, 100, 8);
        Par2Set set = ReadSet(file, volume);

        set.RecoveryBlocks.Should().HaveCount(2);
        Par2Repairer.Repair(file, set, Output(), null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.Repaired);
        File.ReadAllBytes(Output()).Should().Equal(original);
    }

    [Fact]
    public void Repair_IndexAndVolumeCriticalPacketsLost_NoSet()
    {
        (string file, _, string volume) = ProtectWithVolume("a.zip", 960, new Par2Parameters(4, 240, 3));
        string index = Par2Creator.IndexPath(file);
        File.WriteAllBytes(index, new byte[100]);
        byte[] volumeBytes = File.ReadAllBytes(volume);
        foreach (int offset in PacketOffsets(volume).Where(p => p.Type != "PAR 2.0\0RecvSlic").Select(p => p.Offset))
            volumeBytes[offset + 70] ^= 0xFF;
        File.WriteAllBytes(volume, volumeBytes);

        Par2PacketReader.Read([index, volume], CancellationToken.None).Sets.Should().BeEmpty();
    }

    [Fact]
    public void Repair_ForgedRecoveryDataWithValidHash_IsCaughtByTheFinalCheck()
    {
        (string file, _, string volume) = ProtectWithVolume("a.zip", 960, new Par2Parameters(4, 240, 3));
        ForgeRecoveryBlock(volume, 0);
        Damage(file, 0, 4);
        Par2Set set = ReadSet(file, volume);
        string output = Output();

        Par2Repairer.Repair(file, set, output, null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.VerificationFailed);

        File.Exists(output).Should().BeFalse();
        Directory.GetFiles(_temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void Repair_OutputExists_ThrowsAndKeepsIt()
    {
        (string file, Par2Set set) = Protect("a.zip", 10000, 5);
        Damage(file, 10, 1);
        string output = Output();
        File.WriteAllText(output, "keep");

        Action act = () => Par2Repairer.Repair(file, set, output, null, CancellationToken.None);

        act.Should().Throw<IOException>();
        File.ReadAllText(output).Should().Be("keep");
        Directory.GetFiles(_temp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Repair_OutputFolderNotWritable_ThrowsAndLeavesTheOriginal()
    {
        (string file, Par2Set set) = Protect("a.zip", 10000, 5);
        Damage(file, 10, 1);
        byte[] damaged = File.ReadAllBytes(file);
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "locked")).FullName;
        using var locked = new NoCreateFolder(folder);

        Action act = () => Par2Repairer.Repair(file, set, Path.Combine(folder, "a.repaired.zip"), null, CancellationToken.None);

        act.Should().Throw<UnauthorizedAccessException>();
        File.ReadAllBytes(file).Should().Equal(damaged);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.95)]
    public void Repair_CancelledAtAnyPoint_LeavesNoFiles(double at)
    {
        (string file, Par2Set set) = Protect("a.zip", 300001, 10);
        Damage(file, 1000, 5000);
        string[] before = Directory.GetFiles(_temp.Path);
        using var cts = new CancellationTokenSource();
        if (at == 0.0)
            cts.Cancel();

        Action act = () => Par2Repairer.Repair(file, set, Output(), f => { if (f >= at) cts.Cancel(); }, cts.Token);

        act.Should().Throw<OperationCanceledException>();
        Directory.GetFiles(_temp.Path).Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.95)]
    public void Create_CancelledAtAnyPoint_LeavesNoFiles(double at)
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 300001);
        using var cts = new CancellationTokenSource();

        Action act = () => Par2Creator.Create(file, Par2Creator.ChooseParameters(300001, 10)!.Value, f => { if (f >= at) cts.Cancel(); }, cts.Token);

        act.Should().Throw<OperationCanceledException>();
        Directory.GetFiles(_temp.Path).Should().Equal(file);
    }

    // 32768 slices all damaged: missing x (slices + missing) = 2^31 entries, past the repair limit.
    [Theory]
    [InlineData(Par2Limits.MaxInputSlices, true)]
    [InlineData(Par2Limits.MaxInputSlices - 1, false)]
    public void Verify_EverySliceOfTheLargestSetDamaged_IsTooLargeOrNotRepairable(int blocks, bool enoughBlocks)
    {
        Par2VerifyStatus expected = enoughBlocks ? Par2VerifyStatus.RepairTooLarge : Par2VerifyStatus.NotRepairable;
        const int sliceCount = Par2Limits.MaxInputSlices;
        string file = Path.Combine(_temp.Path, "a.zip");
        File.WriteAllBytes(file, new byte[sliceCount * 4]);
        var set = new Par2Set
        {
            SetId = 1,
            SliceSize = 4,
            FileId = 2,
            FileMd5 = 3,
            Md5First16k = 4,
            FileLength = sliceCount * 4,
            Name = "a.zip"u8.ToArray(),
            Slices = new Par2SliceChecksum[sliceCount],
            RecoveryBlocks = [.. Enumerable.Range(0, blocks).Select(e => new Par2RecoveryBlock((uint)e, file, 0))],
        };

        Par2Verification result = Par2Verifier.Verify(file, set, null, CancellationToken.None);

        result.Status.Should().Be(expected);
        result.DamagedSlices.Should().HaveCount(sliceCount);
        Par2Repairer.Repair(file, set, Output(), null, CancellationToken.None).Status
            .Should().Be(expected == Par2VerifyStatus.RepairTooLarge ? Par2RepairStatus.RepairTooLarge : Par2RepairStatus.NotRepairable);
    }

    // --- Misuse ---

    [Fact]
    public void Verify_SetOfAnotherFile_IsNotRepairable()
    {
        (_, Par2Set set) = Protect("a.zip", 10000, 5);
        string other = Path.Combine(_temp.Path, "other.zip");
        File.WriteAllBytes(other, new byte[10000]);

        Par2Verifier.Verify(other, set, null, CancellationToken.None).Status.Should().Be(Par2VerifyStatus.NotRepairable);
    }

    [Fact]
    public void Verify_IndexOnlyAndDamaged_IsNotRepairable()
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 10000);
        Par2CreateResult created = Par2Creator.Create(file, Par2Creator.ChooseParameters(10000, 5)!.Value, null, CancellationToken.None);
        Damage(file, 10, 1);
        Par2Set set = ReadSet(file, created.IndexPath);

        Par2Verifier.Verify(file, set, null, CancellationToken.None).Status.Should().Be(Par2VerifyStatus.NotRepairable);
    }

    // --- Helpers ---

    private string Output() => Path.Combine(_temp.Path, "a.repaired.zip");

    private (string File, Par2Set Set) Protect(string name, int length, int percent, long? sliceSize = null)
    {
        Par2Parameters parameters = sliceSize is { } size
            ? new Par2Parameters(size, (int)Par2FileHasher.SliceCount(length, size), (int)Math.Max(1, Par2FileHasher.SliceCount(Par2FileHasher.SliceCount(length, size) * percent, 100)))
            : Par2Creator.ChooseParameters(length, percent)!.Value;
        (string file, Par2Set set, _) = ProtectWithVolume(name, length, parameters);
        return (file, set);
    }

    private (string File, Par2Set Set, string Volume) ProtectWithVolume(string name, int length, Par2Parameters parameters)
    {
        string file = Par2TestData.WriteContent(_temp.Path, name, length);
        Par2CreateResult created = Par2Creator.Create(file, parameters, null, CancellationToken.None);
        return (file, ReadSet(file, created.IndexPath, created.VolumePath), created.VolumePath);
    }

    private static Par2Set ReadSet(string file, params string[] par2Files) =>
        Par2SetLocator.Select(Par2PacketReader.Read(par2Files, CancellationToken.None).Sets, file)!.Set;

    private static void Damage(string file, int offset, int count)
    {
        using FileStream stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite);
        byte[] bytes = new byte[count];
        stream.Position = offset;
        int read = stream.Read(bytes);
        for (int i = 0; i < read; i++)
            bytes[i] ^= 0xA5;
        stream.Position = offset;
        stream.Write(bytes, 0, read);
    }

    private static List<(int Offset, string Type, uint Exponent)> PacketOffsets(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var packets = new List<(int, string, uint)>();
        for (int offset = 0; offset < bytes.Length;)
        {
            int length = (int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset + 8));
            string type = System.Text.Encoding.ASCII.GetString(bytes, offset + 48, 16).TrimEnd('\0');
            uint exponent = type == "PAR 2.0\0RecvSlic" ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 64)) : 0;
            packets.Add((offset, type, exponent));
            offset += length;
        }
        return packets;
    }

    // Flips a data byte, so the packet MD5 no longer matches and the reader drops the block.
    private static void DestroyRecoveryBlocks(string volume, params uint[] exponents)
    {
        byte[] bytes = File.ReadAllBytes(volume);
        foreach ((int Offset, string Type, uint Exponent) packet in PacketOffsets(volume).Where(p => p.Type == "PAR 2.0\0RecvSlic" && exponents.Contains(p.Exponent)))
            bytes[packet.Offset + 68] ^= 0xFF;
        File.WriteAllBytes(volume, bytes);
    }

    // Changes a recovery block's data and recomputes its packet MD5: wrong data that reads as valid.
    internal static void ForgeRecoveryBlock(string volume, uint exponent)
    {
        byte[] bytes = File.ReadAllBytes(volume);
        (int Offset, string Type, uint Exponent) packet = PacketOffsets(volume).Single(p => p.Type == "PAR 2.0\0RecvSlic" && p.Exponent == exponent);
        int length = (int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(packet.Offset + 8));
        bytes[packet.Offset + 68] ^= 0x01;
        Par2PacketForge.Md5(bytes.AsSpan(packet.Offset + 32, length - 32)).CopyTo(bytes, packet.Offset + 16);
        File.WriteAllBytes(volume, bytes);
    }
}
