using Archiver.Core.Models;
using Archiver.Core.Services;
using FluentAssertions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F280: archives written by the reference tool must never trip Test's local/central header
/// check — plain, AES-256 (AE-2, CRC 0 on both sides) and stdin (Zip64 sentinels in the local
/// header). Uses the vendored 7za.exe (see <see cref="SevenZipRunner"/>); not a timing test.
/// </summary>
public sealed class ZipHeaderCheckCorpusTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("pakko-headers-").FullName;
    private readonly ZipArchiveService _sut = new(new GroupPolicyOptions());

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { /* best-effort */ }
    }

    private string Source()
    {
        string dir = Path.Combine(_temp, "src");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "text.txt"), string.Concat(Enumerable.Repeat("7-zip ", 400)));
        byte[] random = new byte[200_000];
        new Random(280).NextBytes(random);
        File.WriteAllBytes(Path.Combine(dir, "sub", "random.bin"), random);
        return dir;
    }

    private async Task AssertNoHeaderErrorAsync(string archive, string? password = null)
    {
        ArchiveResult result = await _sut.TestAsync([archive],
            resolvePasswordAsync: password is null ? null : _ => Task.FromResult(new PasswordDecision { Password = password }));
        result.Errors.Should().BeEmpty(because: string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public async Task SevenZipPlainZip_NoHeaderError()
    {
        string archive = Path.Combine(_temp, "plain.zip");
        SevenZipRunner.Archive(Source(), archive);

        await AssertNoHeaderErrorAsync(archive);
    }

    [Fact]
    public async Task SevenZipAes256Zip_NoHeaderError()
    {
        string archive = Path.Combine(_temp, "aes.zip");
        SevenZipRunner.ArchiveEncrypted(archive, "Passw0rd", store: false, Source());

        await AssertNoHeaderErrorAsync(archive, "Passw0rd");
    }

    [Fact]
    public async Task SevenZipStdInZip_NoHeaderError()
    {
        string archive = Path.Combine(_temp, "stdin.zip");
        byte[] data = new byte[300_000];
        new Random(2801).NextBytes(data);
        SevenZipRunner.ArchiveFromStdIn(archive, data);

        await AssertNoHeaderErrorAsync(archive);
    }
}
