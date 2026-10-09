using Archiver.Core.Recovery;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Recovery;

// T-F275: the sets Pakko writes. Packets are compared byte for byte with sets par2cmdline 1.4.0
// wrote over the same bytes with the same slice size and recovery count (Fixtures/par2).
public sealed class Par2CreatorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("tiny.bin", 22, 4, 3)]
    [InlineData("data.bin", 300001, 4096, 4)]
    [InlineData("\u0430\u0440\u0445\u0456\u0432.zip", 5000, 512, 2)]
    public void Create_SameParametersAsPar2cmdline_WritesTheSamePackets(string name, int length, int sliceSize, int recoveryCount)
    {
        string file = Par2TestData.WriteContent(_temp.Path, name, length);
        var parameters = new Par2Parameters(sliceSize, (int)Par2FileHasher.SliceCount(length, sliceSize), recoveryCount);

        Par2CreateResult result = Par2Creator.Create(file, parameters, null, CancellationToken.None);

        string golden = Path.Combine(Par2TestData.GoldenDir, name);
        Path.GetFileName(result.IndexPath).Should().Be(name + ".par2");
        Path.GetFileName(result.VolumePath).Should().Be($"{name}.vol0+{recoveryCount}.par2");
        Par2TestData.NonCreatorPackets(result.IndexPath, result.VolumePath)
            .Should().BeEquivalentTo(Par2TestData.NonCreatorPackets(golden + ".par2", $"{golden}.vol0+{recoveryCount}.par2"));
    }

    [Fact]
    public void Create_IndexFile_HoldsTheCriticalPacketsAndCreator()
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 1000);

        Par2CreateResult result = Par2Creator.Create(file, Par2Creator.ChooseParameters(1000, 5)!.Value, null, CancellationToken.None);

        Par2TestData.Packets(result.IndexPath).Select(Par2TestData.TypeOf)
            .Should().Equal("PAR 2.0\0Main", "PAR 2.0\0FileDesc", "PAR 2.0\0IFSC", "PAR 2.0\0Creator");
    }

    [Fact]
    public void Create_VolumeFile_HasCriticalPacketsAroundTheRecoveryBlocks()
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 1000);

        Par2CreateResult result = Par2Creator.Create(file, new Par2Parameters(100, 10, 3), null, CancellationToken.None);

        Par2TestData.Packets(result.VolumePath).Select(Par2TestData.TypeOf).Should().Equal(
            "PAR 2.0\0Main", "PAR 2.0\0FileDesc", "PAR 2.0\0IFSC",
            "PAR 2.0\0RecvSlic", "PAR 2.0\0RecvSlic", "PAR 2.0\0RecvSlic",
            "PAR 2.0\0Main", "PAR 2.0\0FileDesc", "PAR 2.0\0IFSC", "PAR 2.0\0Creator");
    }

    [Fact]
    public void Create_ReportsProgressUpToOne()
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 100000);
        var reports = new List<double>();

        Par2Creator.Create(file, Par2Creator.ChooseParameters(100000, 10)!.Value, reports.Add, CancellationToken.None);

        reports.Should().NotBeEmpty().And.BeInAscendingOrder();
        reports[^1].Should().Be(1.0);
    }

    [Theory]
    [InlineData(1, "a.zip.vol0+1.par2")]
    [InlineData(9, "a.zip.vol0+9.par2")]
    [InlineData(10, "a.zip.vol00+10.par2")]
    [InlineData(100, "a.zip.vol000+100.par2")]
    [InlineData(1974, "a.zip.vol0000+1974.par2")]
    public void VolumePath_PadsLikePar2cmdline(int recoveryCount, string expected)
    {
        Path.GetFileName(Par2Creator.VolumePath(@"C:\x\a.zip", recoveryCount)).Should().Be(expected);
    }

    // The 5 % rows were checked against par2cmdline 1.4.0's own choice (`par2 c -r5 -n1`): the slice
    // size in its Main packet and the count in its volume name (2026-10-09).
    [Theory]
    [InlineData(22L, 5, 4L, 6, 1)]
    [InlineData(300001L, 5, 152L, 1974, 99)]
    [InlineData(300001L, 100, 152L, 1974, 1974)]
    [InlineData(536870912L, 5, 268436L, 2000, 100)]
    [InlineData(1L, 1, 4L, 1, 1)]
    public void ChooseParameters_FollowsPar2cmdlineDefaults(long length, int percent, long sliceSize, int sliceCount, int recoveryCount)
    {
        Par2Creator.ChooseParameters(length, percent).Should().Be(new Par2Parameters(sliceSize, sliceCount, recoveryCount));
    }

    [Fact]
    public void ChooseParameters_EmptyFile_IsNull()
    {
        Par2Creator.ChooseParameters(0, 5).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ChooseParameters_PercentOutOfRange_Throws(int percent)
    {
        Action act = () => Par2Creator.ChooseParameters(1000, percent);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // The writer must never produce a set its own reader refuses (Par2Limits), over the whole range.
    [Theory]
    [InlineData(1L)]
    [InlineData(16384L)]
    [InlineData(4L << 30)]
    [InlineData(2000L << 30)]
    [InlineData(8L << 40)]
    [InlineData(32L << 40)]
    public void ChooseParameters_AnySize_StaysInsideTheReaderLimits(long length)
    {
        foreach (int percent in new[] { 1, 5, 10, 20, 100 })
        {
            Par2Parameters p = Par2Creator.ChooseParameters(length, percent)!.Value;
            p.SliceSize.Should().BePositive().And.BeLessThanOrEqualTo(Par2Limits.MaxSliceSize);
            (p.SliceSize % 4).Should().Be(0);
            p.SliceCount.Should().BeInRange(1, Par2Limits.MaxInputSlices);
            p.RecoveryCount.Should().BeInRange(1, Par2Limits.MaxInputSlices);
            ((long)p.SliceCount * p.SliceSize).Should().BeGreaterThanOrEqualTo(length);
        }
    }

    [Fact]
    public void ChooseParameters_BeyondTheLimits_IsNull()
    {
        Par2Creator.ChooseParameters((32L << 40) + 1, 5).Should().BeNull();
    }

    [Fact]
    public void Create_Cancelled_LeavesNoFiles()
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 100000);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Action act = () => Par2Creator.Create(file, Par2Creator.ChooseParameters(100000, 5)!.Value, null, cts.Token);

        act.Should().Throw<OperationCanceledException>();
        Directory.GetFiles(_temp.Path).Should().Equal(file);
    }

    [Fact]
    public void Create_ExistingSet_IsReplaced()
    {
        string file = Par2TestData.WriteContent(_temp.Path, "a.zip", 1000);
        Par2Parameters parameters = Par2Creator.ChooseParameters(1000, 5)!.Value;
        File.WriteAllText(Par2Creator.IndexPath(file), "stale");

        Par2CreateResult result = Par2Creator.Create(file, parameters, null, CancellationToken.None);

        Par2TestData.Packets(result.IndexPath).Should().HaveCount(4);
    }
}
