using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// Step 4 of T-F52's build order: formalizes Phase 0's confirmed finding (see DECISIONS.md) that a
// regular (non-LPAC) AppContainer with an empty capability list can launch "tar.exe --version"
// successfully, reading its own System32 DLL dependencies with no extra grant needed. Uses its
// own throwaway profile name — never the shared production "Pakko.TarSandbox" profile.
public sealed class SecurityCapabilitiesAttributeListTests : IDisposable
{
    private readonly AppContainerProfile _profile =
        new("Pakko.TarSandbox.Test." + Guid.NewGuid());

    public void Dispose()
    {
        try { _profile.Delete(); } catch { }
    }

    [Fact]
    public async Task RunAsync_TarExeVersionInsideAppContainer_ExitsZeroWithVersionOutput()
    {
        _profile.EnsureExists();
        using var sid = _profile.GetSid();
        using var securityCapabilities = SecurityCapabilitiesAttributeList.Create(sid);

        var (exitCode, stdOut, stdErr) = await SandboxedProcessLauncher.RunAsync(
            @"C:\Windows\System32\tar.exe",
            ["--version"],
            securityCapabilities.AttributeList,
            jobObject: null,
            CancellationToken.None);

        exitCode.Should().Be(0);
        stdOut.Should().Contain("bsdtar");
        stdErr.Should().BeEmpty();
    }
}
