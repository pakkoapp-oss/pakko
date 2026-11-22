using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.Versioning;
using Archiver.Core.IO;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F559: a file or folder inside a source whose plain path opens a device: "NUL" on every Windows,
/// "CON.txt" on Windows 10 and Server 2022 (made through \\?\, WSL or a share). Read by that path, a
/// ZIP held an empty entry "src///./NUL" and the folder counted done; hash gave CRC 0 for "\\.\NUL"; the
/// console blocked. Such an entry is now refused where it is met (T-F558 refused it only as a path given
/// to Core), its bytes never replaced by the device's. Below a \\?\ root every path is literal, and ZIP
/// reads the file itself.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("TarSandbox")]
public sealed class DeviceNamedEntryTests : IDisposable
{
    private const string Literal = @"\\?\";
    private const string DeviceBytes = "DEVICE-NAMED BYTES";
    private readonly TempDirectory _temp = new();
    private readonly PakkoServices _services = PakkoServices.Create(new GroupPolicyOptions());

    public void Dispose()
    {
        foreach (string file in Directory.GetFiles(Literal + _temp.Path, "*", SearchOption.AllDirectories))
            File.Delete(file);
        _temp.Dispose();
        if (Directory.Exists(Literal + _temp.Path))
            Directory.Delete(Literal + _temp.Path, recursive: true);
    }

    // "nul" everywhere; the names only Windows 10 and Server 2022 open as a device where they do.
    public static TheoryData<string> DeviceNames()
    {
        var data = new TheoryData<string> { "nul" };
        foreach (string name in new[] { "CON.txt", "aux.log", "COM1" })
            if (Path.GetFullPath(@"C:\x\" + name).StartsWith(@"\\.\", StringComparison.Ordinal))
                data.Add(name);
        return data;
    }

    public static TheoryData<string, int, string> ZipRoutes()
    {
        var data = new TheoryData<string, int, string>();
        foreach (string name in DeviceNames())
            foreach ((string writer, int files) in new[] { ("sequential", 3), ("many", 70), ("fastest", 3), ("separate", 3), ("separate", 70) })
                data.Add(writer, files, name);
        return data;
    }

    [Theory, MemberData(nameof(ZipRoutes))]
    public async Task Create_ZipFolderHoldingADeviceName_RefusesItInEveryRoute(string writer, int files, string name)
    {
        string src = Folder(Path.Combine("z" + writer + files, "src"));
        string device = WriteLiteral(Path.Combine(src, name), DeviceBytes);
        var expected = new Dictionary<string, string>();
        for (int i = 0; i < files; i++)
        {
            WriteLiteral(Path.Combine(src, $"f{i:D2}.txt"), "OK " + i);
            expected[$"src/f{i:D2}.txt"] = "OK " + i;
        }

        ArchiveResult result = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [src], DestinationFolder = Path.Combine(_temp.Path, "out-z" + writer + files), ArchiveName = "x",
            CompressionLevel = writer == "fastest" ? CompressionLevel.Fastest : CompressionLevel.Optimal,
            Mode = writer == "separate" ? ArchiveMode.SeparateArchives : ArchiveMode.SingleArchive,
        });

        RefusedInside(result, src, device);
        Contents(result, ArchiveContainerFormat.Zip).Should().Equal(expected);
    }

    // A folder "NUL" is not entered: listing it would list the device.
    [Theory]
    [InlineData(ArchiveContainerFormat.Zip)]
    [InlineData(ArchiveContainerFormat.TarGz)]
    public async Task Create_FolderHoldingADeviceNamedFolder_NeverArchivesWhatIsBelowIt(ArchiveContainerFormat format)
    {
        string src = Folder(Path.Combine("df" + format, "src"));
        string device = Path.Combine(src, "NUL");
        WriteLiteral(Path.Combine(device, "in.txt"), DeviceBytes);
        WriteLiteral(Path.Combine(src, "ok.txt"), "OK");
        string other = WriteLiteral(Path.Combine(_temp.Path, "df" + format, "other.txt"), "OTHER");

        ArchiveResult result = await CreateAsync(format, src, other);

        result.FullyProcessedSources.Should().NotContain(src);
        result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.SourceNotFound);
        Dictionary<string, string> contents = Contents(result, format);
        contents.Values.Should().NotContain(DeviceBytes);
        contents["other.txt"].Should().Be("OTHER");
        if (format == ArchiveContainerFormat.Zip)
        {
            result.Errors[0].SourcePath.Should().Be(device);
            contents["src/ok.txt"].Should().Be("OK");
        }
    }

    // tar.exe would read the name itself, but what Core checks of the names it meets (the filter, links, the
    // code page, hard-link pairs) opens them by their plain paths: the whole source is refused, as for a
    // name Windows trims (T-F509), before tar.exe runs.
    [Theory, MemberData(nameof(DeviceNames))]
    public async Task Create_TarFolderHoldingADeviceName_IsRefusedWhole(string name)
    {
        string src = Folder(Path.Combine("t" + name, "src"));
        WriteLiteral(Path.Combine(src, name), DeviceBytes);
        WriteLiteral(Path.Combine(src, "ok.txt"), "OK");
        string other = WriteLiteral(Path.Combine(_temp.Path, "t" + name, "other.txt"), "OTHER");

        ArchiveResult result = await CreateAsync(ArchiveContainerFormat.TarGz, src, other);

        ArchiveError error = result.Errors.Should().ContainSingle().Subject;
        error.Text!.Code.Should().Be(MessageCode.SourceNotFound);
        error.SourcePath.Should().Be(src);
        result.FullyProcessedSources.Should().NotContain(src);
        Contents(result, ArchiveContainerFormat.TarGz).Should().Equal(new Dictionary<string, string> { ["other.txt"] = "OTHER" });
    }

    [Theory, MemberData(nameof(DeviceNames))]
    public async Task Hash_FolderHoldingADeviceName_IsAnErrorNeverTheDevicesHash(string name)
    {
        string src = Folder(Path.Combine("h" + name, "src"));
        string device = WriteLiteral(Path.Combine(src, name), DeviceBytes);
        WriteLiteral(Path.Combine(src, "ok.txt"), "OK");

        HashResult result = await FileHashService.ComputeAsync([src], HashAlgorithmKind.Crc32, null, CancellationToken.None);

        HashEntry entry = result.Entries.Should().ContainSingle(e => e.ErrorText != null).Subject;
        entry.SourcePath.Should().Be(device);
        entry.ErrorText!.Code.Should().Be(MessageCode.SourceNotFound);
        result.Entries.Should().NotContain(e => e.SourcePath.StartsWith(@"\\.\", StringComparison.Ordinal));
    }

    // The deletion's second line: such a source is never listed as complete, so it is never deleted.
    [Theory, MemberData(nameof(DeviceNames))]
    public void SourceTree_FolderHoldingADeviceName_IsNotReadInFull(string name)
    {
        string src = Folder(Path.Combine("s" + name, "src"));
        WriteLiteral(Path.Combine(src, name), DeviceBytes);
        WriteLiteral(Path.Combine(src, "ok.txt"), "OK");

        SourceTree.List(src).Unreadable.Should().BeTrue();
    }

    // Written as \\?\ the source's every path is literal: ZIP reads the file itself.
    [Theory, MemberData(nameof(DeviceNames))]
    public async Task Create_ZipLiteralFolderHoldingADeviceName_ArchivesTheFile(string name)
    {
        string src = Folder(Path.Combine("l" + name, "src"));
        WriteLiteral(Path.Combine(src, name), DeviceBytes);
        WriteLiteral(Path.Combine(src, "ok.txt"), "OK");

        ArchiveResult result = await CreateAsync(ArchiveContainerFormat.Zip, Literal + src);

        result.Errors.Should().BeEmpty();
        Contents(result, ArchiveContainerFormat.Zip).Should().Equal(new Dictionary<string, string>
        {
            ["src/" + name] = DeviceBytes,
            ["src/ok.txt"] = "OK",
        });
    }

    private static void RefusedInside(ArchiveResult result, string source, string device)
    {
        ArchiveError error = result.Errors.Should().ContainSingle().Subject;
        error.Text!.Code.Should().Be(MessageCode.SourceNotFound);
        error.SourcePath.Should().Be(device);
        result.FullyProcessedSources.Should().NotContain(source);
    }

    private string Folder(string relative)
    {
        string folder = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string WriteLiteral(string path, string content)
    {
        Directory.CreateDirectory(Literal + Path.GetDirectoryName(path));
        File.WriteAllText(Literal + path, content);
        return path;
    }

    private Task<ArchiveResult> CreateAsync(ArchiveContainerFormat format, params string[] sources) =>
        _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = sources, DestinationFolder = Path.Combine(_temp.Path, "out-" + Guid.NewGuid().ToString("N")[..6]), ArchiveName = "x", Format = format,
        });

    // Entry name -> content; folder entries are left out.
    private static Dictionary<string, string> Contents(ArchiveResult result, ArchiveContainerFormat format)
    {
        result.CreatedFiles.Should().ContainSingle();
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        if (format == ArchiveContainerFormat.Zip)
        {
            using ZipArchive zip = ZipFile.OpenRead(result.CreatedFiles[0]);
            foreach (ZipArchiveEntry entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
            {
                using var reader = new StreamReader(entry.Open());
                contents.Add(entry.FullName, reader.ReadToEnd());
            }
            return contents;
        }
        using Stream file = File.OpenRead(result.CreatedFiles[0]);
        using Stream data = format == ArchiveContainerFormat.TarGz ? new GZipStream(file, CompressionMode.Decompress) : file;
        using var tar = new TarReader(data);
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.DataStream is null)
                continue;
            using var reader = new StreamReader(entry.DataStream);
            contents.Add(entry.Name, reader.ReadToEnd());
        }
        return contents;
    }
}
