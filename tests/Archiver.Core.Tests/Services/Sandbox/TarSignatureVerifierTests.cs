using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// Step 7 of T-F52's build order: validates TarSignatureVerifier independently against the real
// tar.exe (should pass) and a non-Microsoft-signed decoy (should fail) — see DECISIONS.md's T-F52
// entry for the real signer subject confirmed on this machine
// ("CN=Microsoft Windows, O=Microsoft Corporation, ...").
public sealed class TarSignatureVerifierTests
{
    [Fact]
    public void Verify_RealTarExe_ReturnsTrue()
    {
        TarSignatureVerifier.Verify(@"C:\Windows\System32\tar.exe").Should().BeTrue();
    }

    [Fact]
    public void Verify_CatalogSignedSystemBinary_ReturnsFalse()
    {
        // TarSignatureVerifier only checks embedded (PKCS#7-in-PE) Authenticode signatures.
        // notepad.exe is genuinely Microsoft-signed but via a separate Windows catalog (.cat)
        // file, not an embedded signature — confirmed via
        // `(Get-AuthenticodeSignature notepad.exe).SignatureType` = "Catalog" on this machine.
        // This is a real, deliberate scope boundary, not a bug: tar.exe (the only file this class
        // ever checks) carries a real embedded signature.
        TarSignatureVerifier.Verify(@"C:\Windows\System32\notepad.exe").Should().BeFalse();
    }

    [Fact]
    public void Verify_UnsignedDecoyExecutable_ReturnsFalse()
    {
        string decoyPath = Path.Combine(AppContext.BaseDirectory, "Archiver.Core.Tests.dll");

        TarSignatureVerifier.Verify(decoyPath).Should().BeFalse();
    }

    [Fact]
    public void Verify_NonExistentFile_ReturnsFalseWithoutThrowing()
    {
        Action act = () => TarSignatureVerifier.Verify(Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid() + ".exe"));

        act.Should().NotThrow();
        TarSignatureVerifier.Verify(Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid() + ".exe")).Should().BeFalse();
    }
}
