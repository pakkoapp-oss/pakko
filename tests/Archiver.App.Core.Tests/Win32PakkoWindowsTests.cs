using FluentAssertions;

namespace Archiver.App.Core.Tests;

public sealed class Win32PakkoWindowsTests
{
    // T-F287: drives the real EnumWindows callback thunk (a delegate passed as a function pointer).
    [Fact]
    public void OtherWindowCorners_RunsTheRealEnumeration_WithoutThrowing()
    {
        Action act = () => Win32PakkoWindows.OtherWindowCorners();

        act.Should().NotThrow();
    }
}
