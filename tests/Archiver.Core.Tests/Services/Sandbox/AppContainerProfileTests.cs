using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// Uses its own distinct test-only profile name and is allowed to delete only that test profile
// at teardown. The production "Pakko.TarSandbox" profile (AppContainerProfile.ProductionProfileName)
// is never created or deleted by any test — production code creates it once, lazily, and never
// deletes it (see DECISIONS.md's T-F52 follow-up entry). Do not "fix" this test to exercise the
// shared production profile name.
public sealed partial class AppContainerProfileTests : IDisposable
{
    private readonly string _testProfileName = "Pakko.TarSandbox.Test." + Guid.NewGuid();
    private readonly AppContainerProfile _sut;

    public AppContainerProfileTests() => _sut = new AppContainerProfile(_testProfileName);

    public void Dispose()
    {
        try { _sut.Delete(); } catch { }
    }

    // T-F523: a scope's profile must be new; an existing one would be a sandbox two operations share.
    [Fact]
    public void Create_AnExistingName_Throws()
    {
        _sut.Create();

        Action again = () => _sut.Create();

        again.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryDelete_RemovesTheProfile()
    {
        _sut.Create();

        _sut.TryDelete().Should().BeTrue();

        _sut.IsRegisteredOnWindows().Should().BeFalse();
    }

    // T-F523: a killed run's profile goes; a running owner's, a name of another shape, and one whose
    // owner is not a tag, stay. The owner is the profile's display name: in a packaged process a
    // profile name may have 50 characters (measured), too few for the tag and a unique part.
    [Fact]
    public void SweepStale_RemovesOnlyAnEndedOwnersProfiles()
    {
        string prefix = "Pakko.Sweep" + Guid.NewGuid().ToString("N")[..4] + ".";
        var ended = new AppContainerProfile(prefix + Hex24());
        var running = new AppContainerProfile(prefix + Hex24());
        var otherShape = new AppContainerProfile(prefix + "someone-elses-profile");
        var noTag = new AppContainerProfile(prefix + Hex24());
        ended.Create("m0123abcd-111-222");
        running.Create("m0123abcd-333-444");
        otherShape.Create("m0123abcd-111-222");
        noTag.Create("a display name");
        try
        {
            AppContainerProfile.SweepStale(prefix, tag => tag == "m0123abcd-333-444");

            ended.IsRegisteredOnWindows().Should().BeFalse();
            running.IsRegisteredOnWindows().Should().BeTrue();
            otherShape.IsRegisteredOnWindows().Should().BeTrue();
            noTag.IsRegisteredOnWindows().Should().BeTrue();
        }
        finally
        {
            _ = ended.TryDelete();
            _ = running.TryDelete();
            _ = otherShape.TryDelete();
            _ = noTag.TryDelete();
        }
    }

    private static string Hex24() => Guid.NewGuid().ToString("N")[..24];

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

        using SafeSidHandle sid = _sut.GetSid();

        sid.IsInvalid.Should().BeFalse();
    }

    [Fact]
    public void GetSid_CalledTwice_ReturnsSameSidValueBothTimes()
    {
        _sut.EnsureExists();

        using SafeSidHandle sidA = _sut.GetSid();
        using SafeSidHandle sidB = _sut.GetSid();

        NativeSidToString(sidA).Should().Be(NativeSidToString(sidB));
    }

    // Real, deterministic Win32 setup failure (not simulated) — CreateAppContainerProfile
    // documents a 64-character limit on pszAppContainerName. This is the exact failure shape
    // TarSandboxScope.CreateAsync now catches and rewraps as SandboxSetupException (T-F52) so a
    // blocked/misconfigured sandbox surfaces as an ArchiveError instead of an unhandled crash.
    [Fact]
    public void EnsureExists_NameExceedsMaxLength_ThrowsInvalidOperationException()
    {
        var tooLong = new AppContainerProfile(new string('x', 300));

        Action act = () => tooLong.EnsureExists();

        act.Should().Throw<InvalidOperationException>();
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

    [System.Runtime.InteropServices.LibraryImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool ConvertSidToStringSidW(IntPtr sid, out IntPtr stringSid);

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr LocalFree(IntPtr hMem);
}
