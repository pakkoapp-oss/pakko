using System.Buffers.Binary;
using System.Text;
using Archiver.Core.Recovery;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;
using static Archiver.Core.Tests.Recovery.Par2PacketForge;

namespace Archiver.Core.Tests.Recovery;

// T-F275: reading PAR2 files as untrusted input. The hostile packets carry a correct packet MD5,
// so each test reaches the field it is about instead of stopping at the MD5 check.
public sealed class Par2PacketReaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static Par2ReadResult Read(params string[] paths) => Par2PacketReader.Read(paths, CancellationToken.None);

    // --- Happy path ---

    [Theory]
    [InlineData("tiny.bin", 22, 4, 3)]
    [InlineData("data.bin", 300001, 4096, 4)]
    [InlineData("\u0430\u0440\u0445\u0456\u0432.zip", 5000, 512, 2)]
    public void Read_Par2cmdlineSet_HasEveryField(string name, long length, long sliceSize, int recoveryCount)
    {
        string golden = Path.Combine(Par2TestData.GoldenDir, name);

        Par2ReadResult result = Read(golden + ".par2", $"{golden}.vol0+{recoveryCount}.par2");

        result.Rejected.Should().BeEmpty();
        Par2Set set = result.Sets.Should().ContainSingle().Subject;
        set.SliceSize.Should().Be(sliceSize);
        set.FileLength.Should().Be(length);
        Encoding.UTF8.GetString(set.Name).Should().Be(name);
        set.Slices.Should().HaveCount((int)Par2FileHasher.SliceCount(length, sliceSize));
        set.RecoveryBlocks.Select(b => b.Exponent).Should().Equal(Enumerable.Range(0, recoveryCount).Select(e => (uint)e));
        set.RecoveryBlocks.Should().OnlyContain(b => b.Path.EndsWith($".vol0+{recoveryCount}.par2", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_IndexOnly_HasNoRecoveryBlocks()
    {
        Par2ReadResult result = Read(Path.Combine(Par2TestData.GoldenDir, "data.bin.par2"));

        result.Sets.Should().ContainSingle().Which.RecoveryBlocks.Should().BeEmpty();
    }

    [Fact]
    public void Read_SameVolumeTwice_CountsEachBlockOnce()
    {
        string volume = Path.Combine(Par2TestData.GoldenDir, "tiny.bin.vol0+3.par2");
        string copy = Path.Combine(_temp.Path, "copy.par2");
        File.Copy(volume, copy);

        Read(volume, copy).Sets.Should().ContainSingle().Which.RecoveryBlocks.Should().HaveCount(3);
    }

    [Fact]
    public void Read_RecoveryBlock_DataOffsetPointsAtTheSliceData()
    {
        (_, _, List<byte[]> packets) = OneFileSet(8, 16, "a.zip", recovery: 2);
        string path = Write(_temp.Path, "a.par2", packets);

        Par2RecoveryBlock block = Read(path).Sets.Single().RecoveryBlocks[1];

        byte[] file = File.ReadAllBytes(path);
        file.AsSpan((int)block.DataOffset, 8).ToArray().Should().OnlyContain(b => b == 1);
    }

    // --- Security & boundary ---

    [Fact]
    public void Read_BlockCountWrapsAThirtyTwoBitCounter_IsRefused()
    {
        // GHSA-3c2j-rccw-j2vj: 4 * 2^32 bytes in 4-byte slices is 2^32 slices, 0 in a u32.
        (_, _, List<byte[]> packets) = OneFileSet(4, 4UL << 32, "a.zip");

        Par2ReadResult result = Read(Write(_temp.Path, "a.par2", packets));

        result.Sets.Should().BeEmpty();
        result.Rejected.Should().ContainSingle().Which.Problem.Should().Be(Par2SetProblem.Malformed);
    }

    [Fact]
    public void Read_OneSliceMoreThanTheLimit_IsRefused()
    {
        (_, _, List<byte[]> packets) = OneFileSet(4, 4UL * Par2Limits.MaxInputSlices + 1, "a.zip");

        Read(Write(_temp.Path, "a.par2", packets)).Rejected.Should().ContainSingle().Which.Problem.Should().Be(Par2SetProblem.Malformed);
    }

    [Fact]
    public void Read_ExactlyTheSliceLimit_IsAccepted()
    {
        (_, _, List<byte[]> packets) = OneFileSet(4, 4UL * Par2Limits.MaxInputSlices, "a.zip");

        Read(Write(_temp.Path, "a.par2", packets)).Sets.Should().ContainSingle().Which.Slices.Should().HaveCount(Par2Limits.MaxInputSlices);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(6L)]
    [InlineData(-4L)]
    [InlineData((1L << 30) + 4)]
    public void Read_SliceSizeOutOfRange_IsRefused(long sliceSize)
    {
        byte[] fileId = new byte[16];
        byte[] main = MainBody(sliceSize, fileId);
        string path = Write(_temp.Path, "a.par2", [Packet(Md5(main), MainType, main)]);

        Read(path).Rejected.Should().ContainSingle().Which.Problem.Should().Be(Par2SetProblem.Malformed);
    }

    [Fact]
    public void Read_FileLengthAboveSignedRange_IsRefused()
    {
        byte[] md5 = new byte[16];
        byte[] forgedId = FileId(md5, ulong.MaxValue, "a.zip");
        byte[] main = MainBody(1L << 30, forgedId);
        byte[] forgedSet = Md5(main);
        string path = Write(_temp.Path, "a.par2",
        [
            Packet(forgedSet, MainType, main),
            Packet(forgedSet, FileDescType, FileDescBody(forgedId, md5, md5, ulong.MaxValue, "a.zip")),
        ]);

        Read(path).Sets.Should().BeEmpty();
    }

    [Fact]
    public void Read_LengthBeyondTheFile_PacketIsIgnored()
    {
        (_, _, List<byte[]> packets) = OneFileSet(8, 16, "a.zip");
        BinaryPrimitives.WriteUInt64LittleEndian(packets[0].AsSpan(8), 1UL << 40);

        Read(Write(_temp.Path, "a.par2", packets)).Rejected.Should().ContainSingle().Which.Problem.Should().Be(Par2SetProblem.MissingCriticalPackets);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(60UL)]
    [InlineData(66UL)]
    public void Read_LengthBelowHeaderOrUnaligned_PacketIsIgnored(ulong length)
    {
        (_, _, List<byte[]> packets) = OneFileSet(8, 16, "a.zip");
        BinaryPrimitives.WriteUInt64LittleEndian(packets[0].AsSpan(8), length);

        Read(Write(_temp.Path, "a.par2", packets)).Sets.Should().BeEmpty();
    }

    [Fact]
    public void Read_RecoveryBodyNotSliceSized_BlockIsIgnored()
    {
        (byte[] setId, _, List<byte[]> packets) = OneFileSet(8, 16, "a.zip");
        packets.Add(Packet(setId, RecoveryType, RecoveryBody(0, 12)));
        packets.Add(Packet(setId, RecoveryType, RecoveryBody(1, 4)));

        Read(Write(_temp.Path, "a.par2", packets)).Sets.Single().RecoveryBlocks.Should().BeEmpty();
    }

    [Fact]
    public void Read_IfscWithWrongEntryCount_IsRefused()
    {
        (byte[] setId, byte[] fileId, List<byte[]> packets) = OneFileSet(8, 16, "a.zip");
        packets[2] = Packet(setId, IfscType, IfscBody(fileId, 3));

        Read(Write(_temp.Path, "a.par2", packets)).Rejected.Should().ContainSingle().Which.Problem.Should().Be(Par2SetProblem.Malformed);
    }

    [Fact]
    public void Read_TwoFilesInTheSet_IsRefused()
    {
        byte[] main = MainBody(8, new byte[16], Enumerable.Repeat((byte)1, 16).ToArray());

        Read(Write(_temp.Path, "a.par2", [Packet(Md5(main), MainType, main)]))
            .Rejected.Should().ContainSingle().Which.Problem.Should().Be(Par2SetProblem.MultipleFiles);
    }

    [Fact]
    public void Read_NonRecoveryFileListed_IsRefused()
    {
        byte[] main = MainBody(8, new byte[16], new byte[16]);
        BinaryPrimitives.WriteUInt32LittleEndian(main.AsSpan(8), 1);

        Read(Write(_temp.Path, "a.par2", [Packet(Md5(main), MainType, main)]))
            .Rejected.Should().ContainSingle().Which.Problem.Should().Be(Par2SetProblem.MultipleFiles);
    }

    [Fact]
    public void Read_SetIdNotTheMainBodyHash_MainIsIgnored()
    {
        (_, byte[] fileId, _) = OneFileSet(8, 16, "a.zip");
        byte[] wrongSet = Enumerable.Repeat((byte)7, 16).ToArray();
        string path = Write(_temp.Path, "a.par2",
        [
            Packet(wrongSet, MainType, MainBody(8, fileId)),
            Packet(wrongSet, FileDescType, FileDescBody(fileId, new byte[16], new byte[16], 16, "a.zip")),
            Packet(wrongSet, IfscType, IfscBody(fileId, 2)),
        ]);

        Par2ReadResult result = Read(path);

        result.Sets.Should().BeEmpty();
        result.Rejected.Should().ContainSingle().Which.Problem.Should().Be(Par2SetProblem.MissingCriticalPackets);
    }

    [Fact]
    public void Read_FileDescNotMatchingItsFileId_IsIgnored()
    {
        (byte[] setId, byte[] fileId, List<byte[]> packets) = OneFileSet(8, 16, "a.zip");
        packets[1] = Packet(setId, FileDescType, FileDescBody(fileId, new byte[16], new byte[16], 16, "b.zip"));

        Read(Write(_temp.Path, "a.par2", packets)).Rejected.Should().ContainSingle().Which.Problem.Should().Be(Par2SetProblem.MissingCriticalPackets);
    }

    [Fact]
    public void Read_TwoSetsMixed_ReadsEachSeparately()
    {
        (byte[] SetId, byte[] FileId, List<byte[]> Packets) first = OneFileSet(8, 16, "a.zip", recovery: 1);
        (byte[] SetId, byte[] FileId, List<byte[]> Packets) second = OneFileSet(12, 24, "a.zip", recovery: 2);

        Par2ReadResult result = Read(Write(_temp.Path, "a.par2", [.. first.Packets, .. second.Packets]));

        result.Sets.Select(s => s.RecoveryBlocks.Count).Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void Read_ConflictingIfscCopies_IsRefused()
    {
        (byte[] setId, byte[] fileId, List<byte[]> packets) = OneFileSet(8, 16, "a.zip");
        byte[] other = IfscBody(fileId, 2);
        other[20] = 1;
        packets.Add(Packet(setId, IfscType, other));

        Read(Write(_temp.Path, "a.par2", packets)).Rejected.Should().ContainSingle().Which.Problem.Should().Be(Par2SetProblem.ConflictingPackets);
    }

    [Fact]
    public void Read_ConflictingFileDescCopies_IsRefused()
    {
        (byte[] setId, byte[] fileId, List<byte[]> packets) = OneFileSet(8, 16, "a.zip");
        byte[] otherMd5 = Enumerable.Repeat((byte)9, 16).ToArray();
        packets.Add(Packet(setId, FileDescType, FileDescBody(fileId, otherMd5, new byte[16], 16, "a.zip")));

        Read(Write(_temp.Path, "a.par2", packets)).Rejected.Should().ContainSingle().Which.Problem.Should().Be(Par2SetProblem.ConflictingPackets);
    }

    [Fact]
    public void Read_ConflictingRecoveryBlocksForOneExponent_UsesNeither()
    {
        (byte[] setId, _, List<byte[]> packets) = OneFileSet(8, 16, "a.zip", recovery: 2);
        packets.Add(Packet(setId, RecoveryType, RecoveryBody(1, 8, 0xEE)));

        Read(Write(_temp.Path, "a.par2", packets)).Sets.Single().RecoveryBlocks.Select(b => b.Exponent).Should().Equal(0u);
    }

    [Fact]
    public void Read_UnknownPacketType_IsSkipped()
    {
        (byte[] setId, _, List<byte[]> packets) = OneFileSet(8, 16, "a.zip", recovery: 1);
        packets.Insert(1, Packet(setId, "PAR 2.0\0UniFileN"u8.ToArray(), new byte[32]));

        Read(Write(_temp.Path, "a.par2", packets)).Sets.Should().ContainSingle();
    }

    [Fact]
    public void Read_TruncatedLastPacket_KeepsTheOthers()
    {
        (_, _, List<byte[]> packets) = OneFileSet(8, 16, "a.zip", recovery: 2);
        byte[] all = packets.SelectMany(p => p).ToArray();
        string path = Path.Combine(_temp.Path, "a.par2");
        File.WriteAllBytes(path, all[..^5]);

        Read(path).Sets.Single().RecoveryBlocks.Select(b => b.Exponent).Should().Equal(0u);
    }

    [Fact]
    public void Read_GarbageBetweenPackets_IsSkipped()
    {
        (_, _, List<byte[]> packets) = OneFileSet(8, 16, "a.zip", recovery: 1);
        byte[] garbage = [1, 2, 3, .. Magic, 0xFF, 0xFF, 5];

        Read(Write(_temp.Path, "a.par2", [garbage, packets[0], garbage, packets[1], packets[2], [9], packets[3]]))
            .Sets.Single().RecoveryBlocks.Should().ContainSingle();
    }

    [Fact]
    public void Read_MagicInsideRecoveryData_StillReadsTheBlock()
    {
        (byte[] setId, _, List<byte[]> packets) = OneFileSet(16, 32, "a.zip");
        byte[] body = RecoveryBody(0, 16);
        Magic.CopyTo(body, 6);
        packets.Add(Packet(setId, RecoveryType, body));

        Read(Write(_temp.Path, "a.par2", packets)).Sets.Single().RecoveryBlocks.Should().ContainSingle();
    }

    [Fact]
    public void Read_PacketSpanningTheReadWindow_IsFound()
    {
        (_, _, List<byte[]> packets) = OneFileSet(8, 16, "a.zip");
        byte[] padding = new byte[(1 << 20) - 5];

        Read(Write(_temp.Path, "a.par2", [padding, .. packets])).Sets.Should().ContainSingle();
    }

    [Fact]
    public void Read_SmallPacketAboveTheSizeCap_IsNotRead()
    {
        (byte[] setId, byte[] fileId, List<byte[]> packets) = OneFileSet(8, 16, "a.zip");
        byte[] huge = new byte[(int)Par2Limits.MaxSmallPacketLength];
        fileId.CopyTo(huge, 0);
        packets[2] = Packet(setId, IfscType, huge);

        Read(Write(_temp.Path, "a.par2", packets)).Rejected.Should().ContainSingle().Which.Problem.Should().Be(Par2SetProblem.MissingCriticalPackets);
    }

    // Every 64 bytes a header claiming a 64 KiB IFSC with a wrong MD5: without a budget each one is
    // read and hashed in full, 3072 x 64 KiB for this 256 KiB file.
    [Fact]
    public void Read_FakeHeadersEverywhere_HashingStaysWithinTwiceTheFileLength()
    {
        const int fileLength = 256 * 1024;
        byte[] file = new byte[fileLength];
        for (int offset = 0; offset + 64 <= fileLength; offset += 64)
        {
            Magic.CopyTo(file, offset);
            BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(offset + 8), 64 * 1024);
            IfscType.CopyTo(file, offset + 48);
        }
        string path = Path.Combine(_temp.Path, "fake.par2");
        File.WriteAllBytes(path, file);

        Par2ReadResult result = Read(path);

        result.Sets.Should().BeEmpty();
        result.HashedBytes.Should().BeLessThanOrEqualTo(2L * fileLength);
    }

    [Fact]
    public void Read_ValidSet_HashesNoMoreThanItsFiles()
    {
        string golden = Path.Combine(Par2TestData.GoldenDir, "data.bin");
        string[] files = [golden + ".par2", golden + ".vol0+4.par2"];

        Read(files).HashedBytes.Should().BeLessThanOrEqualTo(files.Sum(f => new FileInfo(f).Length));
    }

    [Theory]
    [InlineData(@"..\..\evil.exe")]
    [InlineData(@"C:\Windows\x.dll")]
    [InlineData("a.zip:stream")]
    [InlineData("CON")]
    public void Read_HostileName_IsKeptAsBytesOnly(string name)
    {
        (_, _, List<byte[]> packets) = OneFileSet(8, 16, name);

        Encoding.UTF8.GetString(Read(Write(_temp.Path, "a.par2", packets)).Sets.Single().Name).Should().Be(name);
    }

    // --- Misuse & error ---

    [Fact]
    public void Read_EmptyFile_FindsNothing()
    {
        string path = Path.Combine(_temp.Path, "empty.par2");
        File.WriteAllBytes(path, []);

        Par2ReadResult result = Read(path);

        result.Sets.Should().BeEmpty();
        result.Rejected.Should().BeEmpty();
    }

    [Fact]
    public void Read_NotAPar2File_FindsNothing()
    {
        Read(Par2TestData.WriteContent(_temp.Path, "a.zip", 5000)).Sets.Should().BeEmpty();
    }

    [Fact]
    public void Read_MissingFile_CountsItUnreadable()
    {
        Read(Path.Combine(_temp.Path, "missing.par2")).UnreadableFiles.Should().Be(1);
    }

    [Fact]
    public void Read_RecoveryBlocksWithoutTheirSet_AreRejected()
    {
        (_, _, List<byte[]> packets) = OneFileSet(8, 16, "a.zip", recovery: 2);

        Read(Write(_temp.Path, "a.par2", packets.Skip(3))).Sets.Should().BeEmpty();
    }

    [Fact]
    public void Read_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Action act = () => Par2PacketReader.Read([Path.Combine(Par2TestData.GoldenDir, "data.bin.par2")], cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }
}
