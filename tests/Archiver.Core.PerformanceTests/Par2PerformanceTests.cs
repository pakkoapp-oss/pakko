using System.Diagnostics;
using Archiver.Core.Recovery;
using FluentAssertions;
using Xunit.Abstractions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F275: PAR2 speed against par2cmdline 1.4.0 (downloaded by scripts/Get-Par2Oracles.ps1) on
/// the default shape — 512 MiB, 5 %, about 2000 slices and 100 recovery blocks. Release only: the
/// vector kernel is several times slower without the JIT optimizer, while par2cmdline is native.
/// </summary>
public sealed class Par2PerformanceTests(ITestOutputHelper output) : IDisposable
{
    private const long Length = 512L << 20;
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    [Trait("Category", "VeryLarge")]
    public void Create_DefaultShape_NotSlowerThanPar2cmdline()
    {
        ReleaseBuildGuard.RequireOptimizedCore();
        string par2cmdline = Par2cmdlinePath();
        File.Exists(par2cmdline).Should().BeTrue("run scripts/Get-Par2Oracles.ps1 first");
        string mine = WriteRandom(Path.Combine(_temp.Path, "mine.zip"));
        string theirsFolder = Directory.CreateDirectory(Path.Combine(_temp.Path, "theirs")).FullName;
        File.Copy(mine, Path.Combine(theirsFolder, "a.zip"));
        Par2Parameters parameters = Par2Creator.ChooseParameters(Length, 5)!.Value;

        var watch = Stopwatch.StartNew();
        Par2Creator.Create(mine, parameters, null, CancellationToken.None);
        TimeSpan pakko = watch.Elapsed;

        watch.Restart();
        using (Process process = Process.Start(new ProcessStartInfo(par2cmdline, "c -q -r5 -n1 a.zip.par2 a.zip")
               { WorkingDirectory = theirsFolder, UseShellExecute = false, CreateNoWindow = true })!)
        {
            process.WaitForExit();
            process.ExitCode.Should().Be(0);
        }
        TimeSpan reference = watch.Elapsed;

        output.WriteLine($"PAR2 create 512 MiB / 5 % ({parameters}) — Pakko: {pakko}, par2cmdline: {reference}");
        pakko.Should().BeLessThanOrEqualTo(reference, "the plan's target is par2cmdline's speed or better");
    }

    [Fact]
    [Trait("Category", "VeryLarge")]
    public void Repair_DefaultShape_HundredSlicesMissing_Timed()
    {
        ReleaseBuildGuard.RequireOptimizedCore();
        string file = WriteRandom(Path.Combine(_temp.Path, "a.zip"));
        Par2Parameters parameters = Par2Creator.ChooseParameters(Length, 5)!.Value;
        Par2CreateResult created = Par2Creator.Create(file, parameters, null, CancellationToken.None);
        using (FileStream stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite))
        {
            for (int slice = 0; slice < parameters.RecoveryCount; slice++)
            {
                stream.Position = slice * 19L * parameters.SliceSize;
                stream.WriteByte(0);
            }
        }
        Par2Set set = Par2SetLocator.Select(Par2PacketReader.Read([created.IndexPath, created.VolumePath], CancellationToken.None).Sets, file)!.Set;

        var watch = Stopwatch.StartNew();
        Par2RepairResult result = Par2Repairer.Repair(file, set, Path.Combine(_temp.Path, "a.repaired.zip"), null, CancellationToken.None);

        output.WriteLine($"PAR2 repair 512 MiB, {result.RepairedSlices} slices: {watch.Elapsed}");
        result.Status.Should().Be(Par2RepairStatus.Repaired);
    }

    // Offsets past 4 GiB: every position and length on the way is a long.
    [Fact]
    [Trait("Category", "VeryLarge")]
    public void CreateVerifyRepair_FileOver4GiB_RoundTrips()
    {
        ReleaseBuildGuard.RequireOptimizedCore();
        const long length = (4L << 30) + 4097;
        string file = Path.Combine(_temp.Path, "big.zip");
        using (FileStream stream = File.Create(file))
        {
            stream.SetLength(length);
            stream.Position = length - 3;
            stream.Write([1, 2, 3]);
        }
        Par2Parameters parameters = Par2Creator.ChooseParameters(length, 1)!.Value;
        Par2CreateResult created = Par2Creator.Create(file, parameters, null, CancellationToken.None);
        Par2Set set = Par2SetLocator.Select(Par2PacketReader.Read([created.IndexPath, created.VolumePath], CancellationToken.None).Sets, file)!.Set;
        Par2Verifier.Verify(file, set, null, CancellationToken.None).Status.Should().Be(Par2VerifyStatus.Intact);
        using (FileStream stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = length - 2;
            stream.WriteByte(0xFF);
        }

        string repaired = Path.Combine(_temp.Path, "big.repaired.zip");
        Par2Repairer.Repair(file, set, repaired, null, CancellationToken.None).Should().Be(new Par2RepairResult(Par2RepairStatus.Repaired, 1));

        using FileStream check = File.OpenRead(repaired);
        check.Length.Should().Be(length);
        check.Position = length - 3;
        byte[] tail = new byte[3];
        check.ReadExactly(tail);
        tail.Should().Equal(1, 2, 3);
        output.WriteLine($"PAR2 round trip {length} bytes ({parameters})");
    }

    private static string WriteRandom(string path)
    {
        var random = new Random(512);
        byte[] buffer = new byte[1 << 20];
        using FileStream stream = File.Create(path);
        for (long done = 0; done < Length; done += buffer.Length)
        {
            random.NextBytes(buffer);
            stream.Write(buffer);
        }
        return path;
    }

    private static string Par2cmdlinePath()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "global.json")))
                return Path.Combine(dir.FullName, "artifacts", "par2-oracles", "par2cmdline", "par2.exe");
        }
        throw new DirectoryNotFoundException("Repository root (global.json) not found");
    }
}
