using System.Diagnostics;
using System.Text;
using Archiver.Core.Interfaces;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F442: every read-only format tar.exe opens beyond the tar family, through Pakko's own path
/// (the routers), success and failure. Samples are written at test time by this system's tar.exe
/// (and makecab for CAB). The expectation follows the installed tar.exe: a format it reads must
/// extract, list and test; one it does not must be refused with a reason, never handed to it.
/// Run on both builds (the double tar test): bsdtar 3.8.9 here, 3.5.2 on PakkoW10.
/// </summary>
[Collection("TarSandbox")]
public sealed class ReadOnlyTarFormatsTests : IDisposable
{
    private const string TarExe = @"C:\Windows\System32\tar.exe";
    private const string MakeCab = @"C:\Windows\System32\makecab.exe";

    private readonly PakkoServices _services = PakkoServices.Create(new GroupPolicyOptions());
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<string> Formats => ["iso", "cab", "newc", "odc", "ar", "compress", "lzip"];

    private async Task<TarCapabilities> CapabilitiesAsync() => await _services.TarService.DetectCapabilitiesAsync();

    private static bool Reads(string format, TarCapabilities caps) => format switch
    {
        "cab" => caps.SupportsCab,
        "lzip" => caps.SupportsLzip,
        _ => caps.SupportsBuiltInReaders,
    };

    // Formats whose data carries a checksum tar.exe checks (measured, docs/DECISIONS.md).
    private static bool ChecksContents(string format) => format is "cab" or "lzip";

    private string Source()
    {
        string source = Path.Combine(_temp.Path, "src");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.txt"), "alpha");
        File.WriteAllBytes(Path.Combine(source, "b.bin"), RandomBytes(40_000, 442));
        return source;
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static (int ExitCode, string StdErr) Run(string exe, string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = workingDirectory };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> stdOut = process.StandardOutput.ReadToEndAsync();
        string stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        _ = stdOut.Result;
        return (process.ExitCode, stdErr);
    }

    // A sample of a.txt and b.bin, or null when this tar.exe cannot write the format (lzip on
    // 3.5.2): the caller then builds a file that only carries the format's signature.
    private string? Sample(string format)
    {
        string source = Source();
        string archive = Path.Combine(_temp.Path, "sample." + format);
        (int exit, string err) = format switch
        {
            "cab" => MakeCabinet(source, archive, [("a.txt", "a.txt"), ("b.bin", "b.bin")]),
            "compress" => Run(TarExe, source, "-cZf", archive, "a.txt", "b.bin"),
            "lzip" => Run(TarExe, source, "--lzip", "-cf", archive, "a.txt", "b.bin"),
            "iso" => Run(TarExe, source, "--format=iso9660", "-cf", archive, "a.txt", "b.bin"),
            _ => Run(TarExe, source, "--format=" + format, "-cf", archive, "a.txt", "b.bin"),
        };
        if (exit != 0)
        {
            format.Should().Be("lzip", err);
            return null;
        }
        return archive;
    }

    private static (int, string) MakeCabinet(string workingDirectory, string archive, (string From, string Name)[] files)
    {
        string ddf = Path.Combine(workingDirectory, "cab.ddf");
        var lines = new List<string>
        {
            ".Set CabinetNameTemplate=" + Path.GetFileName(archive),
            ".Set DiskDirectoryTemplate=" + Path.GetDirectoryName(archive),
            ".Set InfFileName=NUL", ".Set RptFileName=NUL",
        };
        lines.AddRange(files.Select(f => $"\"{f.From}\" \"{f.Name}\""));
        File.WriteAllLines(ddf, lines, Encoding.ASCII);
        return Run(MakeCab, workingDirectory, "/F", ddf);
    }

    private async Task<string> SampleOrSignatureAsync(string format)
    {
        if (Sample(format) is { } archive)
            return archive;
        (await CapabilitiesAsync()).SupportsLzip.Should().BeFalse("a tar.exe that reads lzip should write it too");
        string signatureOnly = Path.Combine(_temp.Path, "sig.tar.lz");
        File.WriteAllBytes(signatureOnly, [.. "LZIP"u8, 1, 0x14, .. RandomBytes(200, 1)]);
        return signatureOnly;
    }

    private string NewDestination() => Path.Combine(_temp.Path, "out-" + Guid.NewGuid().ToString("N")[..8]);

    private async Task<ArchiveResult> ExtractAsync(string archive, string destination)
    {
        IExtractionRouter router = await _services.CreateExtractionRouterAsync();
        return await router.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive], DestinationFolder = destination, Mode = ExtractMode.SingleFolder,
        });
    }

    private static string Errors(ArchiveResult result) => string.Join(" | ", result.Errors.Select(e => e.Message));

    // --- Success, or a refusal with the reason, by what this tar.exe reads ---

    [TarIntegrationTheory]
    [MemberData(nameof(Formats))]
    public async Task Extract_FollowsTheInstalledTar(string format)
    {
        string archive = await SampleOrSignatureAsync(format);
        TarCapabilities caps = await CapabilitiesAsync();
        string destination = NewDestination();

        ArchiveResult result = await ExtractAsync(archive, destination);

        if (Reads(format, caps))
        {
            result.Errors.Should().BeEmpty(Errors(result));
            File.ReadAllText(Path.Combine(destination, "a.txt")).Should().Be("alpha");
            File.ReadAllBytes(Path.Combine(destination, "b.bin")).Should().Equal(RandomBytes(40_000, 442));
        }
        else
        {
            result.SkippedFiles.Should().ContainSingle().Which.Reason.Should().Contain(caps.Version);
            Directory.Exists(destination).Should().BeFalse("a refused archive writes nothing");
        }
    }

    [TarIntegrationTheory]
    [MemberData(nameof(Formats))]
    public async Task List_FollowsTheInstalledTar(string format)
    {
        string archive = await SampleOrSignatureAsync(format);
        TarCapabilities caps = await CapabilitiesAsync();
        IArchiveListingRouter router = await _services.CreateListingRouterAsync();

        ArchiveListResult result = await router.ListEntriesAsync(archive);

        if (Reads(format, caps))
        {
            result.Success.Should().BeTrue(result.ErrorMessage);
            result.Entries.Where(e => !e.IsDirectory).Select(e => e.Path).Should().BeEquivalentTo(["a.txt", "b.bin"]);
        }
        else
        {
            result.Success.Should().BeFalse();
        }
    }

    [TarIntegrationTheory]
    [MemberData(nameof(Formats))]
    public async Task Test_IntactArchive_PassesSayingWhatItChecked(string format)
    {
        string archive = await SampleOrSignatureAsync(format);
        TarCapabilities caps = await CapabilitiesAsync();
        IExtractionRouter router = await _services.CreateExtractionRouterAsync();

        ArchiveResult result = await router.TestAsync([archive]);

        if (!Reads(format, caps))
        {
            (result.Errors.Count + result.SkippedFiles.Count).Should().BeGreaterThan(0, "an unreadable format is never reported as tested");
            return;
        }
        result.Errors.Should().BeEmpty(Errors(result));
        if (ChecksContents(format))
            result.Warnings.Should().BeEmpty();
        else
            result.Warnings.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TestedStructureOnly);
    }

    // --- Failure: damage, a cut file, hostile shapes, a format libarchive cannot read ---

    [TarIntegrationTheory]
    [InlineData("cab")]
    [InlineData("lzip")]
    public async Task Test_DamagedContent_FailsWhereTheFormatChecksIt(string format)
    {
        TarCapabilities caps = await CapabilitiesAsync();
        if (!Reads(format, caps) || Sample(format) is not { } archive)
            return;
        byte[] bytes = File.ReadAllBytes(archive);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(archive, bytes);
        IExtractionRouter router = await _services.CreateExtractionRouterAsync();

        ArchiveResult result = await router.TestAsync([archive]);

        result.Errors.Should().NotBeEmpty();
    }

    [TarIntegrationTheory]
    [MemberData(nameof(Formats))]
    public async Task Extract_CutShort_IsAnErrorNeverASuccess(string format)
    {
        TarCapabilities caps = await CapabilitiesAsync();
        if (!Reads(format, caps) || Sample(format) is not { } archive)
            return;
        byte[] bytes = File.ReadAllBytes(archive);
        int keep = format == "iso" ? 16 * 2048 + 2048 * 3 : bytes.Length / 3;
        File.WriteAllBytes(archive, bytes[..keep]);
        string destination = NewDestination();

        ArchiveResult result = await ExtractAsync(archive, destination);

        result.Errors.Should().NotBeEmpty("a cut " + format + " must not read as a whole archive");
    }

    [TarIntegrationTheory]
    [InlineData("newc", "fifo type=fifo mode=0644")]
    [InlineData("newc", "dev type=char device=native,1,3 mode=0644")]
    [InlineData("newc", "link type=link link=../../outside.txt")]
    [InlineData("odc", "link type=link link=../../outside.txt")]
    [InlineData("iso9660", "link type=link link=../../outside.txt")]
    [InlineData("iso9660", "fifo type=fifo mode=0644")]
    public async Task Extract_SpecialEntry_RefusedNothingWritten(string format, string hostileLine)
    {
        if (!(await CapabilitiesAsync()).SupportsBuiltInReaders)
            return;
        string source = Source();
        File.WriteAllText(Path.Combine(source, "spec.mtree"), "#mtree\na.txt type=file\n" + hostileLine + "\n", Encoding.ASCII);
        string archive = Path.Combine(_temp.Path, "hostile." + format);
        Run(TarExe, source, "--format=" + format, "-cf", archive, "@spec.mtree");
        string destination = NewDestination();

        ArchiveResult result = await ExtractAsync(archive, destination);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarSpecialEntry);
        Directory.Exists(destination).Should().BeFalse();
        File.Exists(Path.Combine(_temp.Path, "outside.txt")).Should().BeFalse();
    }

    [TarIntegrationTheory]
    [InlineData("..\\up.txt")]
    [InlineData("C:\\abs.txt")]
    public async Task Extract_CabinetWithAnEscapingName_RefusedNothingWritten(string hostileName)
    {
        if (!(await CapabilitiesAsync()).SupportsCab)
            return;
        string source = Source();
        string archive = Path.Combine(_temp.Path, "hostile.cab");
        MakeCabinet(source, archive, [("a.txt", "a.txt"), ("a.txt", hostileName)]).Item1.Should().Be(0);
        string destination = NewDestination();

        ArchiveResult result = await ExtractAsync(archive, destination);

        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.TarUnsafeEntryPath);
        Directory.Exists(destination).Should().BeFalse();
        File.Exists(Path.Combine(_temp.Path, "up.txt")).Should().BeFalse();
    }

    // A Windows install ISO is UDF beside an ISO 9660 stub; libarchive reads only the stub.
    [Integration]
    public async Task Extract_UdfImage_RefusedNamingUdf()
    {
        string archive = Sample("iso")!;
        byte[] bytes = File.ReadAllBytes(archive);
        int sector = 16;
        while (Encoding.ASCII.GetString(bytes, sector * 2048 + 1, 5) == "CD001")
            sector++;
        foreach (string id in (string[])["BEA01", "NSR02", "TEA01"])
        {
            Array.Clear(bytes, sector * 2048, 2048);
            Encoding.ASCII.GetBytes(id).CopyTo(bytes, sector * 2048 + 1);
            bytes[sector * 2048 + 6] = 1;
            sector++;
        }
        File.WriteAllBytes(archive, bytes);
        string destination = NewDestination();

        ArchiveResult result = await ExtractAsync(archive, destination);

        result.SkippedFiles.Should().ContainSingle().Which.Reason.Should().Contain("UDF");
        Directory.Exists(destination).Should().BeFalse();
    }

    [Integration]
    public async Task Extract_FileNamedIsoWithoutADiscImage_IsNotHandedToTar()
    {
        string archive = Path.Combine(_temp.Path, "fake.iso");
        File.WriteAllBytes(archive, RandomBytes(100_000, 7));
        string destination = NewDestination();

        ArchiveResult result = await ExtractAsync(archive, destination);

        (result.Errors.Count + result.SkippedFiles.Count).Should().BeGreaterThan(0);
        Directory.Exists(destination).Should().BeFalse();
    }

    // tar.exe reads an ISO cut before its root directory as an empty archive with exit code 0.
    [Integration]
    public async Task Extract_IsoCutBeforeItsDirectory_IsAnError()
    {
        string archive = Sample("iso")!;
        byte[] bytes = File.ReadAllBytes(archive);
        File.WriteAllBytes(archive, bytes[..(19 * 2048)]);
        string destination = NewDestination();

        ArchiveResult result = await ExtractAsync(archive, destination);

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("incomplete");
        Directory.Exists(destination).Should().BeFalse();
    }

    // T-F457 item 2: an image shorter than it declares, but past its directory, is left to tar.exe,
    // which reads it in full when only padding is missing and fails when data is (measured on 3.8.9
    // and 3.5.2, docs/DECISIONS.md "T-F457").
    [Integration]
    public async Task Extract_IsoDeclaringTwoSectorsMoreThanItHolds_ExtractsInFull()
    {
        string archive = Sample("iso")!;
        byte[] bytes = File.ReadAllBytes(archive);
        uint declared = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16 * 2048 + 80));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16 * 2048 + 80), declared + 2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16 * 2048 + 84), declared + 2);
        File.WriteAllBytes(archive, bytes);
        string destination = NewDestination();

        ArchiveResult result = await ExtractAsync(archive, destination);

        result.Errors.Should().BeEmpty(Errors(result));
        File.ReadAllText(Path.Combine(destination, "a.txt")).Should().Be("alpha");
        File.ReadAllBytes(Path.Combine(destination, "b.bin")).Should().Equal(RandomBytes(40_000, 442));
    }

    [Integration]
    public async Task Extract_IsoWithoutItsLastTwoPaddingSectors_ExtractsInFull()
    {
        string archive = Sample("iso")!;
        byte[] bytes = File.ReadAllBytes(archive);
        bytes.AsSpan(bytes.Length - 2 * 2048).ContainsAnyExcept((byte)0).Should().BeFalse("tar.exe pads its images with zero sectors");
        File.WriteAllBytes(archive, bytes[..^(2 * 2048)]);
        string destination = NewDestination();

        ArchiveResult result = await ExtractAsync(archive, destination);

        result.Errors.Should().BeEmpty(Errors(result));
        File.ReadAllBytes(Path.Combine(destination, "b.bin")).Should().Equal(RandomBytes(40_000, 442));
    }

    // T-F457 item 1: tar.exe reads an ISO inside gzip as an ISO, with no UDF refusal and no cut check
    // (measured on 3.8.9 and 3.5.2). Pakko gives the image itself, as 7-Zip does; opening it then
    // takes the bare image's checks.
    private static string GzipOf(string file)
    {
        string gz = file + ".gz";
        using (FileStream input = File.OpenRead(file))
        using (FileStream output = File.Create(gz))
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
            input.CopyTo(gzip);
        return gz;
    }

    [Integration]
    public async Task Extract_IsoInsideGzip_GivesTheImage()
    {
        string image = Sample("iso")!;
        string archive = GzipOf(image);
        string destination = NewDestination();

        ArchiveResult result = await ExtractAsync(archive, destination);

        result.Errors.Should().BeEmpty(Errors(result));
        Directory.GetFileSystemEntries(destination).Select(Path.GetFileName).Should().Equal("sample.iso");
        File.ReadAllBytes(Path.Combine(destination, "sample.iso")).Should().Equal(File.ReadAllBytes(image));
    }

    [Integration]
    public async Task Extract_CutIsoInsideGzip_GivesTheImageWhichIsThenRefused()
    {
        string image = Sample("iso")!;
        File.WriteAllBytes(image, File.ReadAllBytes(image)[..(19 * 2048)]);
        string destination = NewDestination();

        ArchiveResult unwrapped = await ExtractAsync(GzipOf(image), destination);
        ArchiveResult opened = await ExtractAsync(Path.Combine(destination, "sample.iso"), NewDestination());

        unwrapped.Errors.Should().BeEmpty(Errors(unwrapped));
        opened.Errors.Should().ContainSingle().Which.Message.Should().Contain("incomplete");
    }

    [Integration]
    public async Task List_IsoInsideGzip_IsTheImage()
    {
        string archive = GzipOf(Sample("iso")!);
        IArchiveListingRouter router = await _services.CreateListingRouterAsync();

        ArchiveListResult result = await router.ListEntriesAsync(archive);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Entries.Select(e => e.Path).Should().Equal("sample.iso");
    }

    [Integration]
    public async Task Extract_IsoCutInsideFileData_IsAnError()
    {
        string archive = Sample("iso")!;
        byte[] bytes = File.ReadAllBytes(archive);
        int data = bytes.AsSpan().IndexOf(RandomBytes(40_000, 442).AsSpan(0, 64));
        data.Should().BePositive();
        File.WriteAllBytes(archive, bytes[..(data + 4096)]);
        string destination = NewDestination();

        ArchiveResult result = await ExtractAsync(archive, destination);

        result.Errors.Should().NotBeEmpty();
        string extracted = Path.Combine(destination, "b.bin");
        (File.Exists(extracted) && File.ReadAllBytes(extracted).AsSpan().SequenceEqual(RandomBytes(40_000, 442))).Should().BeFalse();
    }

    [Integration]
    public async Task Extract_BlockedByPolicy_RefusedNamingTheFormat()
    {
        string archive = Sample("cab")!;
        IExtractionRouter router = await PakkoServices.Create(new GroupPolicyOptions { BlockedFormats = ["cab"] }).CreateExtractionRouterAsync();
        string destination = NewDestination();

        ArchiveResult result = await router.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [archive], DestinationFolder = destination, Mode = ExtractMode.SingleFolder,
        });

        result.SkippedFiles.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.FormatBlocked);
        Directory.Exists(destination).Should().BeFalse();
    }
}
