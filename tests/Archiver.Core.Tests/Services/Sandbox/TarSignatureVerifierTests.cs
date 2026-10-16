using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// Step 7 of T-F52's build order: validates TarSignatureVerifier independently against the real
// tar.exe (should pass) and a non-Microsoft-signed decoy (should fail) — see DECISIONS.md's T-F52
// entry for the real signer subject confirmed on this machine
// ("CN=Microsoft Windows, O=Microsoft Corporation, ...").
// T-F401: a system binary that carries no embedded signature and is signed only through a Windows
// catalog, as tar.exe is on Windows 10, Windows 11 up to 23H2 and Server 2022, must pass too.
public sealed class TarSignatureVerifierTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "pakko-sigtest-" + Guid.NewGuid().ToString("N"));

    public TarSignatureVerifierTests() => Directory.CreateDirectory(_workDir);

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch (IOException) { /* best-effort cleanup of a temp folder */ }
    }

    [Fact]
    public void Verify_RealTarExe_ReturnsTrue()
    {
        TarSignatureVerifier.Verify(@"C:\Windows\System32\tar.exe").Should().BeTrue();
    }

    [Fact]
    public void Verify_CatalogOnlySystemBinary_ReturnsTrue()
    {
        TarSignatureVerifier.Verify(CatalogOnlySystemBinary()).Should().BeTrue();
    }

    [Fact]
    public void Verify_UnchangedCopyOfCatalogOnlyBinary_ReturnsTrue()
    {
        // A catalog names a file by its hash, not by its path: the same bytes elsewhere pass.
        // Pakko only ever checks C:\Windows\System32\tar.exe, so the path is not the check's job.
        string copy = Path.Combine(_workDir, "copy.exe");
        File.Copy(CatalogOnlySystemBinary(), copy);

        TarSignatureVerifier.Verify(copy).Should().BeTrue();
    }

    [Fact]
    public void Verify_ChangedCopyOfCatalogOnlyBinary_ReturnsFalse()
    {
        string copy = Path.Combine(_workDir, "changed.exe");
        File.Copy(CatalogOnlySystemBinary(), copy);
        FlipByteInsideFirstSection(copy);

        TarSignatureVerifier.Verify(copy).Should().BeFalse();
    }

    [Fact]
    public void Verify_ChangedCopyOfTarExe_ReturnsFalse()
    {
        string copy = Path.Combine(_workDir, "tar.exe");
        File.Copy(@"C:\Windows\System32\tar.exe", copy);
        FlipByteInsideFirstSection(copy);

        TarSignatureVerifier.Verify(copy).Should().BeFalse();
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

    // Chosen by the PE certificate table, not by Get-AuthenticodeSignature's SignatureType: that
    // says "Catalog" for a file that also has an embedded signature (tar.exe on Windows 11 24H2+).
    private static string CatalogOnlySystemBinary()
    {
        string[] candidates = ["whoami.exe", "where.exe", "findstr.exe", "notepad.exe"];
        string? found = candidates
            .Select(name => Path.Combine(Environment.SystemDirectory, name))
            .FirstOrDefault(path => File.Exists(path) && PeImage.CertificateTableSize(path) == 0);
        found.Should().NotBeNull("Windows ships these system binaries signed through a catalog only");
        return found!;
    }

    // The middle of the first section's raw data: inside the hashed image, away from the parts
    // Authenticode leaves out of its hash (the checksum, the certificate table and what follows it).
    private static void FlipByteInsideFirstSection(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        (int rawOffset, int rawSize) = PeImage.FirstSection(bytes);
        int at = rawOffset + rawSize / 2;
        bytes[at] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
    }

    private static class PeImage
    {
        private const int SecurityDirectoryIndex = 4;

        public static int CertificateTableSize(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int optionalHeader = OptionalHeaderOffset(bytes);
            bool pe32Plus = BitConverter.ToUInt16(bytes, optionalHeader) == 0x20B;
            int dataDirectories = optionalHeader + (pe32Plus ? 112 : 96);
            return BitConverter.ToInt32(bytes, dataDirectories + SecurityDirectoryIndex * 8 + 4);
        }

        public static (int RawOffset, int RawSize) FirstSection(byte[] bytes)
        {
            int peHeader = BitConverter.ToInt32(bytes, 0x3C);
            int optionalHeaderSize = BitConverter.ToUInt16(bytes, peHeader + 20);
            int firstSectionHeader = OptionalHeaderOffset(bytes) + optionalHeaderSize;
            int rawSize = BitConverter.ToInt32(bytes, firstSectionHeader + 16);
            int rawOffset = BitConverter.ToInt32(bytes, firstSectionHeader + 20);
            return (rawOffset, rawSize);
        }

        private static int OptionalHeaderOffset(byte[] bytes) => BitConverter.ToInt32(bytes, 0x3C) + 24;
    }
}
