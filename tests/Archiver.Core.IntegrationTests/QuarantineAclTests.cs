using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

/// <summary>
/// Step 6 of T-F52's build order: proves QuarantineAcl's SetEntriesInAclW/SetNamedSecurityInfoW
/// grants actually confine a real sandboxed tar.exe, formalizing Phase 0's icacls-based spike
/// (see DECISIONS.md's T-F52 Phase 0 entry) into the production ACL primitive. Uses its own
/// throwaway AppContainer profile — never the shared production profile name.
/// </summary>
[Collection("TarSandbox")]
public sealed class QuarantineAclTests : IDisposable
{
    private const string TarExecutablePath = @"C:\Windows\System32\tar.exe";

    private readonly TempDirectory _temp = new();
    private readonly AppContainerProfile _profile = new("Pakko.TarSandbox.Test." + Guid.NewGuid());

    public void Dispose()
    {
        _temp.Dispose();
        try { _profile.Delete(); } catch { }
    }

    [Fact]
    public async Task GrantReadExecuteAndModify_RealSandboxedTarExtraction_Succeeds()
    {
        _profile.EnsureExists();
        using var sid = _profile.GetSid();
        using var securityCapabilities = SecurityCapabilitiesAttributeList.Create(sid);

        string inDir = Path.Combine(_temp.Path, "in");
        string outDir = Path.Combine(_temp.Path, "out");
        Directory.CreateDirectory(inDir);
        Directory.CreateDirectory(outDir);

        string archivePath = Path.Combine(inDir, "fixture.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "acl grant test")]);

        // Quarantine root itself needs a traverse-only grant — Phase 0 confirmed an AppContainer
        // identity does not bypass ancestor traverse checking by default.
        QuarantineAcl.GrantTraverseOnly(_temp.Path, sid);
        QuarantineAcl.GrantReadExecute(inDir, sid);
        QuarantineAcl.GrantModify(outDir, sid);

        var (exitCode, _, stdErr) = await SandboxedProcessLauncher.RunAsync(
            TarExecutablePath,
            ["-xf", archivePath, "-C", outDir],
            securityCapabilities.AttributeList,
            jobObject: null,
            CancellationToken.None);

        exitCode.Should().Be(0, because: stdErr);
        File.ReadAllText(Path.Combine(outDir, "a.txt")).Should().Be("acl grant test");
    }

    [Fact]
    public async Task NoGrant_RealSandboxedTarExtraction_FailsWithAccessDenied()
    {
        // Negative control mirroring Phase 0's own — the actual security proof, not just "does
        // the happy path work". A destination folder that never received any ACE for the
        // AppContainer SID must be unreachable, even though Pakko's own (unsandboxed) process
        // created it and can read/write it freely itself.
        _profile.EnsureExists();
        using var sid = _profile.GetSid();
        using var securityCapabilities = SecurityCapabilitiesAttributeList.Create(sid);

        string inDir = Path.Combine(_temp.Path, "in_negative");
        string neverAcldOutDir = Path.Combine(_temp.Path, "out_never_acld");
        Directory.CreateDirectory(inDir);
        Directory.CreateDirectory(neverAcldOutDir);

        string archivePath = Path.Combine(inDir, "fixture.tar");
        ExternalTarFixtureBuilder.CreateCompressedTar(archivePath, "-cf", [("a.txt", "should not be written")]);

        // Grant the quarantine root and "in\" only — deliberately do NOT grant "out_never_acld\".
        QuarantineAcl.GrantTraverseOnly(_temp.Path, sid);
        QuarantineAcl.GrantReadExecute(inDir, sid);

        var (exitCode, _, stdErr) = await SandboxedProcessLauncher.RunAsync(
            TarExecutablePath,
            ["-xf", archivePath, "-C", neverAcldOutDir],
            securityCapabilities.AttributeList,
            jobObject: null,
            CancellationToken.None);

        exitCode.Should().NotBe(0);
        stdErr.Should().Contain("chdir");
        File.Exists(Path.Combine(neverAcldOutDir, "a.txt")).Should().BeFalse();
    }

    // Real, deterministic Win32 setup failure (not simulated) — GetNamedSecurityInfoW fails for a
    // path that doesn't exist. This is the exact failure shape TarSandboxScope.CreateAsync now
    // catches and rewraps as SandboxSetupException (T-F52), so a blocked/misconfigured sandbox
    // surfaces to callers as an ArchiveError instead of an unhandled crash.
    [Fact]
    public void GrantReadExecute_NonexistentPath_ThrowsInvalidOperationException()
    {
        _profile.EnsureExists();
        using var sid = _profile.GetSid();

        string nonexistentPath = Path.Combine(_temp.Path, "does_not_exist_" + Guid.NewGuid());

        Action act = () => QuarantineAcl.GrantReadExecute(nonexistentPath, sid);

        act.Should().Throw<InvalidOperationException>();
    }
}
