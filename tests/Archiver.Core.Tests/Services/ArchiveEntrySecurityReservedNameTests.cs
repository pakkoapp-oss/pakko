using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F243 item 4: Windows treats a name as a device when the part before the FIRST dot (trailing
// spaces ignored) is a device name, in any path segment — the old check looked only at the last
// segment without its last extension, so "CON.a.b", "NUL/x", "CONIN$" and "COM\u00B9" got through.
public sealed class ArchiveEntrySecurityReservedNameTests
{
    [Theory]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("CON.a.b")]
    [InlineData("NUL/x.txt")]
    [InlineData("dir/AUX")]
    [InlineData("dir/prn.tar.gz")]
    [InlineData("CONIN$")]
    [InlineData("conout$.log")]
    [InlineData("COM0")]
    [InlineData("LPT9.x")]
    [InlineData("COM\u00B9")]
    [InlineData("LPT\u00B3.txt")]
    [InlineData("com\u00B2")]
    [InlineData("NUL .txt")]
    [InlineData("a/CON /b.txt")]
    [InlineData(@"dir\NUL\x.txt")]
    public void HasReservedName_DeviceNameInAnySegment_IsReserved(string entryPath)
    {
        ArchiveEntrySecurity.HasReservedName(entryPath).Should().BeTrue();
    }

    [Theory]
    [InlineData("CONSOLE.txt")]
    [InlineData("COM10")]
    [InlineData("LPT")]
    [InlineData("a.CON")]
    [InlineData("CON_x.txt")]
    [InlineData("NULL")]
    [InlineData("dir/readme.txt")]
    [InlineData(".CON")]
    [InlineData("a//b.txt")]
    public void HasReservedName_OrdinaryName_IsNotReserved(string entryPath)
    {
        ArchiveEntrySecurity.HasReservedName(entryPath).Should().BeFalse();
    }

    // T-F228: both separators split segments — pins the Split call against binding to its
    // (char, int count) overload, which would leave '\' unsplit (Sonar S3220).
    [Theory]
    [InlineData(@"..\x.txt")]
    [InlineData(@"a\..\..\x.txt")]
    [InlineData("a/../x.txt")]
    public void HasUnsafePath_DotDotSegmentWithEitherSeparator_IsUnsafe(string entryPath)
    {
        ArchiveEntrySecurity.HasUnsafePath(entryPath).Should().BeTrue();
    }
}
