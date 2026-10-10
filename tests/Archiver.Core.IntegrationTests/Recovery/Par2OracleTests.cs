using System.Buffers.Binary;
using Archiver.Core.Recovery;
using FluentAssertions;
using static Archiver.Core.IntegrationTests.Recovery.Par2Oracle;

namespace Archiver.Core.IntegrationTests.Recovery;

// T-F275: Pakko's sets against par2cmdline 1.4.0, par2cmdline-turbo 1.5.0 and MultiPar's par2j,
// and their sets against Pakko. par2cmdline repairs only with contiguous recovery exponents
// (docs/DECISIONS.md, T-F275); the non-contiguous case goes to turbo and par2j.
public sealed class Par2OracleTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Par2OracleFact(Par2cmdline, Turbo, MultiPar)]
    public void PakkoSet_EveryOracleVerifiesItIntact()
    {
        string file = WriteRandom("a.zip", 500_000);
        Par2CreateResult created = Create(file, 5);

        Run(Par2cmdline, _temp.Path, "v", "-q", "--full-hash", created.IndexPath).ExitCode.Should().Be(0);
        Run(Turbo, _temp.Path, "v", "-q", created.IndexPath).ExitCode.Should().Be(0);
        Run(MultiPar, _temp.Path, "v", created.IndexPath).ExitCode.Should().Be(0);
    }

    // T-F275 step 2: the whole path a frontend takes — the router writes the archive, then its set.
    [Par2OracleFact(Par2cmdline)]
    public async Task ArchiveWithRecoveryData_Par2cmdlineVerifiesTheSet()
    {
        string source = WriteRandom("data.bin", 300_000);
        string destination = Path.Combine(_temp.Path, "out");
        var services = Archiver.Core.Services.PakkoServices.Create(new Archiver.Core.Models.GroupPolicyOptions());

        Archiver.Core.Models.ArchiveResult result = await services.CreationRouter.ArchiveAsync(new Archiver.Core.Models.ArchiveOptions
        {
            SourcePaths = [source],
            DestinationFolder = destination,
            ArchiveName = "data",
            RecoveryPercent = 10,
        });

        result.Errors.Should().BeEmpty();
        result.RecoveryFiles.Should().HaveCount(2);
        Run(Par2cmdline, destination, "v", "-q", "--full-hash", result.RecoveryFiles[0]).ExitCode.Should().Be(0);
    }

    [Par2OracleFact(Par2cmdline, MultiPar)]
    public void PakkoSet_CyrillicName_OraclesVerifyIt()
    {
        string file = WriteRandom("архів.zip", 100_000);
        Par2CreateResult created = Create(file, 5);

        Run(Par2cmdline, _temp.Path, "v", "-q", "--full-hash", created.IndexPath).ExitCode.Should().Be(0);
        Run(MultiPar, _temp.Path, "v", created.IndexPath).ExitCode.Should().Be(0);
    }

    [Par2OracleFact(Par2cmdline)]
    public void PakkoSet_DamagedFile_Par2cmdlineRepairsIt()
    {
        string file = WriteRandom("a.zip", 500_000);
        byte[] original = File.ReadAllBytes(file);
        Par2CreateResult created = Create(file, 5);
        DamageSlices(file, created.Parameters.SliceSize, 3, 500, 1999);

        Run(Par2cmdline, _temp.Path, "r", "-q", created.IndexPath).ExitCode.Should().Be(0);

        File.ReadAllBytes(file).Should().Equal(original);
    }

    [Par2OracleFact(Turbo, MultiPar)]
    public void PakkoSet_DamagedFile_TurboAndPar2jRepairIt()
    {
        foreach (string tool in new[] { Turbo, MultiPar })
        {
            using var folder = new TempDirectory();
            string file = WriteRandom(Path.Combine(folder.Path, "a.zip"), 500_000);
            byte[] original = File.ReadAllBytes(file);
            Par2CreateResult created = Create(file, 5);
            DamageSlices(file, created.Parameters.SliceSize, 0, 1000);

            int exitCode = Run(tool, folder.Path, "r", created.IndexPath).ExitCode;

            exitCode.Should().Be(tool == MultiPar ? 16 : 0, tool);
            File.ReadAllBytes(file).Should().Equal(original, tool);
        }
    }

    // Slices [2, 48, 237] missing, recovery exponents {1, 2, 4, 5} left: par2cmdline takes the
    // first three, which are dependent here, and gives up (exit 6); a solution exists. Slices are
    // 8 bytes, not 4: par2j rebuilds a 4-byte slice from its CRC-32 alone, which hides the case.
    [Par2OracleFact(Par2cmdline, Turbo, MultiPar)]
    public void NonContiguousExponents_PakkoTurboAndPar2jRepair_Par2cmdlineCannot()
    {
        var results = new Dictionary<string, int>();
        foreach (string tool in new[] { Par2cmdline, Turbo, MultiPar, "pakko" })
        {
            using var folder = new TempDirectory();
            string file = WriteRandom(Path.Combine(folder.Path, "a.zip"), 1920);
            byte[] original = File.ReadAllBytes(file);
            Par2CreateResult created = Par2Creator.Create(file, new Par2Parameters(8, 240, 6), null, CancellationToken.None);
            DestroyRecoveryBlocks(created.VolumePath, 0, 3);
            DamageSlices(file, 8, 2, 48, 237);

            if (tool == "pakko")
            {
                Par2Set set = Par2SetLocator.Select(Par2PacketReader.Read([created.IndexPath, created.VolumePath], CancellationToken.None).Sets, file)!.Set;
                string output = Path.Combine(folder.Path, "a.repaired.zip");
                Par2Repairer.Repair(file, set, output, null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.Repaired);
                File.ReadAllBytes(output).Should().Equal(original);
                continue;
            }
            (int exitCode, string log) = Run(tool, folder.Path, "r", created.IndexPath);
            results[tool] = exitCode;
            if (tool != Par2cmdline)
                File.ReadAllBytes(file).Should().Equal(original, $"{tool} exit {exitCode}: {log}");
        }

        results[Par2cmdline].Should().NotBe(0, "par2cmdline 1.4.0 does not select around dependent blocks");
        results[Turbo].Should().Be(0);
        (results[MultiPar] & 16).Should().Be(16, "par2j's exit code is a bit mask: 16 repaired, 256 the destroyed blocks' PAR file incomplete");
    }

    [Par2OracleFact(Par2cmdline)]
    public void Par2cmdlineSet_DamagedFile_PakkoRepairsIt()
    {
        string file = WriteRandom("a.zip", 800_001);
        byte[] original = File.ReadAllBytes(file);
        Run(Par2cmdline, _temp.Path, "c", "-q", "-r10", "a.zip.par2", "a.zip").ExitCode.Should().Be(0);
        Truncate(file, 790_000);

        RepairWithPakko(file).Should().Equal(original);
    }

    [Par2OracleFact(MultiPar)]
    public void Par2jSet_DamagedFile_PakkoRepairsIt()
    {
        string file = WriteRandom("a.zip", 800_001);
        byte[] original = File.ReadAllBytes(file);
        Run(MultiPar, _temp.Path, "c", "/rr10", "a.zip.par2", "a.zip").ExitCode.Should().Be(0);
        DamageSlices(file, 4096, 1, 50);

        RepairWithPakko(file).Should().Equal(original);
    }

    [Par2OracleFact(Par2cmdline)]
    public void SetNamedWithoutTheExtension_PakkoFindsAndRepairsIt()
    {
        string file = WriteRandom("a.zip", 200_000);
        byte[] original = File.ReadAllBytes(file);
        Run(Par2cmdline, _temp.Path, "c", "-q", "-r10", "a.par2", "a.zip").ExitCode.Should().Be(0);
        DamageSlices(file, 100, 7);

        Par2SetLocator.SetFilesForTarget(file).Should().NotBeEmpty().And.OnlyContain(p => Path.GetFileName(p).StartsWith("a.", StringComparison.Ordinal));
        RepairWithPakko(file).Should().Equal(original);
    }

    [Par2OracleFact(Par2cmdline)]
    public void SameParameters_PakkoPacketsEqualPar2cmdline()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "theirs")).FullName;
        string theirs = WriteRandom(Path.Combine(folder, "a.zip"), 1_000_003);
        string mine = Path.Combine(_temp.Path, "a.zip");
        File.Copy(theirs, mine);
        Run(Par2cmdline, folder, "c", "-q", "-s4096", "-c10", "-n1", "a.zip.par2", "a.zip").ExitCode.Should().Be(0);

        Par2CreateResult created = Par2Creator.Create(mine, new Par2Parameters(4096, 245, 10), null, CancellationToken.None);

        NonCreatorPackets(created.IndexPath, created.VolumePath).Should().BeEquivalentTo(
            NonCreatorPackets(Path.Combine(folder, "a.zip.par2"), Path.Combine(folder, "a.zip.vol00+10.par2")));
    }

    private string WriteRandom(string nameOrPath, int length)
    {
        string path = Path.IsPathRooted(nameOrPath) ? nameOrPath : Path.Combine(_temp.Path, nameOrPath);
        byte[] bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static Par2CreateResult Create(string file, int percent) =>
        Par2Creator.Create(file, Par2Creator.ChooseParameters(new FileInfo(file).Length, percent)!.Value, null, CancellationToken.None);

    private static byte[] RepairWithPakko(string file)
    {
        Par2ReadResult read = Par2PacketReader.Read(Par2SetLocator.SetFilesForTarget(file), CancellationToken.None);
        Par2Set set = Par2SetLocator.Select(read.Sets, file)!.Set;
        string output = Path.Combine(Path.GetDirectoryName(file)!, "a.repaired.zip");
        Par2Repairer.Repair(file, set, output, null, CancellationToken.None).Status.Should().Be(Par2RepairStatus.Repaired);
        return File.ReadAllBytes(output);
    }

    private static void DamageSlices(string file, long sliceSize, params int[] slices)
    {
        using FileStream stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite);
        foreach (int slice in slices)
        {
            stream.Position = slice * sliceSize;
            int value = stream.ReadByte();
            stream.Position = slice * sliceSize;
            stream.WriteByte((byte)(value ^ 0xFF));
        }
    }

    private static void Truncate(string file, long length)
    {
        using FileStream stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite);
        stream.SetLength(length);
    }

    private static List<(int Offset, string Type, uint Exponent, int Length)> Packets(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var packets = new List<(int, string, uint, int)>();
        for (int offset = 0; offset < bytes.Length;)
        {
            int length = (int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset + 8));
            string type = System.Text.Encoding.ASCII.GetString(bytes, offset + 48, 16).TrimEnd('\0');
            uint exponent = type == "PAR 2.0\0RecvSlic" ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 64)) : 0;
            packets.Add((offset, type, exponent, length));
            offset += length;
        }
        return packets;
    }

    private static HashSet<string> NonCreatorPackets(params string[] paths) =>
        [.. paths.SelectMany(path =>
        {
            byte[] bytes = File.ReadAllBytes(path);
            return Packets(path).Where(p => p.Type != "PAR 2.0\0Creator").Select(p => Convert.ToHexString(bytes, p.Offset, p.Length));
        })];

    private static void DestroyRecoveryBlocks(string volume, params uint[] exponents)
    {
        byte[] bytes = File.ReadAllBytes(volume);
        foreach ((int Offset, string Type, uint Exponent, int Length) packet in Packets(volume).Where(p => p.Type == "PAR 2.0\0RecvSlic" && exponents.Contains(p.Exponent)))
            bytes[packet.Offset + 68] ^= 0xFF;
        File.WriteAllBytes(volume, bytes);
    }
}
