using Archiver.Core.IO;
using Archiver.Core.Services.Sandbox;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F511 PR 2: the archiveint probe left "pakko-probe-<guid>.tar" in %TEMP% when its run was
// killed, and nothing ever removed it. Its name is TempOwner's now, and each probe sweeps first.
public sealed class ArchiveintProbeFileTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void ProbeFile_IsOwnedByThisRun_AndSweptOnceItsRunIsGone()
    {
        string live = ArchiveintGate.NewProbePath(_temp.Path);
        File.WriteAllBytes(live, new byte[1024]);
        string machine = TempOwner.CurrentTag.Split('-')[0];
        string dead = _temp.CreateFile($"pakko-probe-{machine}-{int.MaxValue}-1-{Guid.NewGuid():N}.tar");
        string legacy = _temp.CreateFile($"pakko-probe-{Guid.NewGuid():N}.tar");
        File.SetCreationTimeUtc(legacy, DateTime.UtcNow.AddDays(-2));
        string other = _temp.CreateFile("pakko-probe-notes.txt");

        ArchiveintGate.SweepStaleProbes(_temp.Path);

        Path.GetFileName(live).Should().StartWith("pakko-probe-" + TempOwner.CurrentTag + "-").And.EndWith(".tar");
        File.Exists(live).Should().BeTrue();
        File.Exists(dead).Should().BeFalse();
        File.Exists(legacy).Should().BeFalse("v1.10's unowned probe file goes once a day old");
        File.Exists(other).Should().BeTrue();
    }
}
