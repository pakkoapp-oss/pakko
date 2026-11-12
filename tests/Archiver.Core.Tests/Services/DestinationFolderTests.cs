using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F475: an archive path Windows opens as a device. A path with no folder above it threw out of
// the engines (ArchiveTempFile's folder was null); one under a device's root went to the device.
public sealed class DestinationFolderTests
{
    [Theory]
    [InlineData(@"\\.\NUL")]
    [InlineData(@"\\.\CON")]
    [InlineData(@"\\.\nul\o.zip")]
    [InlineData(@"\\?\NUL\o.zip")]
    [InlineData(@"\\.\COM1\x\o.zip")]
    public void IsDevice_DeviceNamespacePaths_AreDevices(string path) =>
        DestinationFolder.IsDevice(path).Should().BeTrue();

    [Theory]
    [InlineData(@"C:\x\o.zip")]
    [InlineData(@"\\?\C:\x\o.zip")]
    [InlineData(@"\\.\C:\x\o.zip")]
    [InlineData(@"\\server\share\o.zip")]
    [InlineData(@"\\?\UNC\server\share\o.zip")]
    [InlineData(@"C:\x\NUL.d\o.zip")]
    [InlineData("")]
    [InlineData("a\0b.zip")]
    public void IsDevice_OrdinaryOrUnparsablePaths_AreNotDevices(string path) =>
        DestinationFolder.IsDevice(path).Should().BeFalse();

    // "CON.zip" is a device on Windows 10 and Server 2022 and a file on Windows 11: the check
    // follows the rules of the Windows it runs on, which Path.GetFullPath applies.
    [Theory]
    [InlineData("CON.zip")]
    [InlineData(@"CON\o.zip")]
    public void IsDevice_DeviceNameInAFolder_FollowsThisWindows(string relative)
    {
        string path = Path.Combine(Path.GetTempPath(), relative);
        bool windowsReadsADevice = Path.GetFullPath(path).StartsWith(@"\\.\", StringComparison.Ordinal);

        DestinationFolder.IsDevice(path).Should().Be(windowsReadsADevice);
    }
}
