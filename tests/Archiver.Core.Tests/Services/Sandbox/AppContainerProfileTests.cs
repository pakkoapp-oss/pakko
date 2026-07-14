using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// Uses its own distinct test-only profile name and is allowed to delete only that test profile
// at teardown. The production "Pakko.TarSandbox" profile (AppContainerProfile.ProductionProfileName)
// is never created or deleted by any test — production code creates it once, lazily, and never
// deletes it (see DECISIONS.md's T-F52 follow-up entry). Do not "fix" this test to exercise the
// shared production profile name.
public sealed class AppContainerProfileTests : IDisposable
{
    private readonly string _testProfileName = "Pakko.TarSandbox.Test." + Guid.NewGuid();
    private readonly AppContainerProfile _sut;

    public AppContainerProfileTests() => _sut = new AppContainerProfile(_testProfileName);

    public void Dispose()
    {
        try { _sut.Delete(); } catch { }
    }

    [Fact]
    public void EnsureExists_FirstCall_Succeeds()
    {
        Action act = () => _sut.EnsureExists();
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureExists_CalledTwice_SecondCallToleratesAlreadyExists()
    {
        _sut.EnsureExists();

        Action act = () => _sut.EnsureExists();

        act.Should().NotThrow();
    }

    [Fact]
    public void GetSid_AfterEnsureExists_ReturnsValidSid()
    {
        _sut.EnsureExists();

        using var sid = _sut.GetSid();

        sid.IsInvalid.Should().BeFalse();
    }

    [Fact]
    public void GetSid_CalledTwice_ReturnsSameSidValueBothTimes()
    {
        _sut.EnsureExists();

        using var sidA = _sut.GetSid();
        using var sidB = _sut.GetSid();

        NativeSidToString(sidA).Should().Be(NativeSidToString(sidB));
    }

    private static string NativeSidToString(SafeSidHandle sid)
    {
        if (!ConvertSidToStringSidW(sid.DangerousGetHandle(), out IntPtr stringSidPtr))
            throw new InvalidOperationException("ConvertSidToStringSidW failed.");
        try
        {
            return System.Runtime.InteropServices.Marshal.PtrToStringUni(stringSidPtr)!;
        }
        finally
        {
            // ConvertSidToStringSidW allocates via LocalAlloc — must be freed with LocalFree,
            // not Marshal.FreeHGlobal (relying on FreeHGlobal's LocalAlloc-compatible Windows
            // implementation detail would be fragile).
            LocalFree(stringSidPtr);
        }
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr stringSid);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
