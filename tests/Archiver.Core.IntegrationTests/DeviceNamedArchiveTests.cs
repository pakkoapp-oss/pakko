using System.Runtime.Versioning;
using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// T-F475: a source named "CON.txt" (made through a <c>\\?\</c> path) gets the archive name
/// "CON.zip". On Windows 10 and Server 2022 that name in any folder is the console device: the
/// engines threw (ArchiveTempFile's folder was null), past the hard constraint that a Core method
/// never throws. Now that one archive is refused with its own code and the others are written. On
/// Windows 11 the name is an ordinary file and the archive is written; the canary's Server 2022
/// job runs these with the other rules.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("TarSandbox")]
public sealed class DeviceNamedArchiveTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly PakkoServices _services = PakkoServices.Create(new GroupPolicyOptions());
    private readonly string _deviceNamed;
    private readonly string _ordinary;
    private readonly string _output;

    public DeviceNamedArchiveTests()
    {
        _deviceNamed = Path.Combine(_temp.Path, "CON.txt");
        File.WriteAllText(Literal(_deviceNamed), "con");
        _ordinary = Path.Combine(_temp.Path, "b.txt");
        File.WriteAllText(_ordinary, "b");
        _output = Path.Combine(_temp.Path, "out");
    }

    public void Dispose()
    {
        foreach (string file in Directory.GetFiles(Literal(_temp.Path), "*", SearchOption.AllDirectories))
            File.Delete(file);
        _temp.Dispose();
    }

    [Theory]
    [InlineData(ArchiveContainerFormat.Zip, ".zip")]
    [InlineData(ArchiveContainerFormat.TarGz, ".tar.gz")]
    public async Task SeparateArchives_DeviceNamedArchive_OnlyThatOneIsRefused(ArchiveContainerFormat format, string extension)
    {
        ArchiveResult result = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [SourcePath(_deviceNamed), _ordinary], DestinationFolder = _output, Mode = ArchiveMode.SeparateArchives, Format = format,
        });

        result.CreatedFiles.Select(Path.GetFileName).Should().Contain("b" + extension);
        if (WindowsReadsADevice("CON" + extension))
        {
            result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.ArchiveNameReserved);
            result.CreatedFiles.Should().ContainSingle();
        }
        else
        {
            result.Errors.Should().BeEmpty();
            File.Exists(Literal(Path.Combine(_output, "CON" + extension))).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(ArchiveContainerFormat.Zip, ".zip")]
    [InlineData(ArchiveContainerFormat.TarGz, ".tar.gz")]
    public async Task SingleArchive_DeviceNamedArchive_IsRefusedWhereItIsADevice(ArchiveContainerFormat format, string extension)
    {
        ArchiveResult result = await _services.CreationRouter.ArchiveAsync(new ArchiveOptions
        {
            SourcePaths = [_ordinary], DestinationFolder = _output, ArchiveName = "CON", Format = format,
        });

        if (WindowsReadsADevice("CON" + extension))
        {
            result.Errors.Should().ContainSingle().Which.Text!.Code.Should().Be(MessageCode.ArchiveNameReserved);
            result.CreatedFiles.Should().BeEmpty();
            Directory.Exists(_output).Should().BeFalse("nothing is written, not even the folder");
        }
        else
        {
            result.Errors.Should().BeEmpty();
            File.Exists(Literal(Path.Combine(_output, "CON" + extension))).Should().BeTrue();
        }
    }

    private bool WindowsReadsADevice(string name) =>
        Path.GetFullPath(Path.Combine(_output, name)).StartsWith(@"\\.\", StringComparison.Ordinal);

    private static string Literal(string path) => @"\\?\" + path;

    // The name as a frontend passes it: a plain path where Windows reads a file there, else the literal one.
    private static string SourcePath(string path) =>
        Path.GetFullPath(path).StartsWith(@"\\.\", StringComparison.Ordinal) ? Literal(path) : path;
}
