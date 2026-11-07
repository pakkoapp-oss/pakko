using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Services.Antivirus;
using Archiver.Core.Text;
using FluentAssertions;

namespace Archiver.Core.IntegrationTests;

// T-F465: tar.exe prints names in the system ANSI page; Pakko read them in the regional format's
// page. Where the two differ, a listed name could be another entry's name in Pakko's page, and
// selecting that row sent the other entry's bytes: a selection extracted the wrong file with
// success, and a selection scan reported Clean for a threat (measured on 3.8.9, format en-US over
// system 1251). These tests force the listing's page away from the system's, so the case runs on
// any machine, and expect every name sent back to tar.exe to be refused unless its bytes are the
// ones tar.exe printed. The archive is a pax tar with a volume label, which Pakko's header walk
// does not read: only the guard on the names stands between the listing and the wrong file.
[Collection("TarSandbox")]
public sealed class TarOutputPageTests : IDisposable
{
    private const string ThreatText = "DJE threat";
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [SwapPairIntegration]
    public async Task Extract_SelectionUnderAnotherPage_RefusedNothingWritten()
    {
        SwapFixture fixture = Fixture();
        var sut = new TarSandboxedService(new GroupPolicyOptions()) { ForcedTarOutputEncoding = fixture.Listing };
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [fixture.Path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = [fixture.ShownName],
        });

        result.Success.Should().BeFalse(fixture.Describe());
        (Directory.Exists(dest) ? Directory.GetFiles(dest, "*", SearchOption.AllDirectories) : []).Should().BeEmpty(fixture.Describe());
    }

    [SwapPairIntegration]
    public async Task Scan_SelectionUnderAnotherPage_NeverClean()
    {
        SwapFixture fixture = Fixture();
        var scanner = new FakeAmsiScanner { DetectedText = "DJE" };
        var sut = new AntivirusScanService(new TarCapabilities(), groupPolicyOptions: new GroupPolicyOptions(), () => scanner,
            providerState: () => AmsiProviderState.Usable) { ForcedTarOutputEncoding = fixture.Listing };

        ThreatScanResult result = await sut.ScanAsync(new AntivirusScanOptions { ArchivePaths = [fixture.Path], SelectedEntryPaths = [fixture.ShownName] });

        result.OverallVerdict.Should().NotBe(ThreatVerdict.Clean, fixture.Describe());
    }

    [SwapPairIntegration]
    public async Task Test_FilterUnderAnotherPage_Refused()
    {
        SwapFixture fixture = Fixture();
        var sut = new TarSandboxedService(new GroupPolicyOptions()) { ForcedTarOutputEncoding = fixture.Listing };

        ArchiveResult result = await sut.TestAsync([fixture.Path], entryFilter: (path, _) => path == fixture.ShownName);

        result.Success.Should().BeFalse(fixture.Describe());
    }

    // A filter that leaves the shown name out goes to tar.exe as an exclusion ("-X"): the same guard.
    [SwapPairIntegration]
    public async Task Test_FilterLeavingTheNameOutUnderAnotherPage_Refused()
    {
        SwapFixture fixture = Fixture();
        var sut = new TarSandboxedService(new GroupPolicyOptions()) { ForcedTarOutputEncoding = fixture.Listing };

        ArchiveResult result = await sut.TestAsync([fixture.Path], entryFilter: (path, _) => path != fixture.ShownName);

        result.Success.Should().BeFalse(fixture.Describe());
    }

    // The "-q" first copies: the threat is only in the first of two same-named entries, so only the
    // first-copy pass can reach it, and it sends the listed name back to tar.exe.
    [SwapPairIntegration]
    public async Task Scan_FirstCopyUnderAnotherPage_NeverClean()
    {
        SwapFixture fixture = Fixture(threatTwice: true);
        var scanner = new FakeAmsiScanner { DetectedText = "DJE" };
        var sut = new AntivirusScanService(new TarCapabilities(), groupPolicyOptions: new GroupPolicyOptions(), () => scanner,
            providerState: () => AmsiProviderState.Usable) { ForcedTarOutputEncoding = fixture.Listing };

        ThreatScanResult result = await sut.ScanAsync(new AntivirusScanOptions { ArchivePaths = [fixture.Path] });

        result.OverallVerdict.Should().NotBe(ThreatVerdict.Clean, fixture.Describe());
    }

    // The control: in the page tar.exe prints in, the same selection extracts the threat itself.
    [SwapPairIntegration]
    public async Task Extract_SelectionInTheSystemPage_TheSelectedFile()
    {
        SwapFixture fixture = Fixture();
        var sut = new TarSandboxedService(new GroupPolicyOptions());
        string dest = Path.Combine(_temp.Path, "out");

        ArchiveResult result = await sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [fixture.Path], DestinationFolder = dest, Mode = ExtractMode.SingleFolder,
            SelectedEntryPaths = [fixture.ThreatName],
        });

        result.Errors.Should().BeEmpty(fixture.Describe());
        File.ReadAllText(Path.Combine(dest, fixture.ThreatName)).Should().Be(ThreatText, fixture.Describe());
    }

    private sealed record SwapFixture(string Path, string ThreatName, string ShownName, Encoding Listing)
    {
        public string Describe() =>
            $"{TarCodePage.Describe()}; listing read as {Listing.CodePage}; threat {Hex(ThreatName)} is listed as {Hex(ShownName)}";

        private static string Hex(string s) => string.Join(' ', s.Select(c => ((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)));
    }

    // Two names a swap needs, in this machine's ANSI page P: the threat "a" + P(b), which a listing
    // read in page W shows as "a" + W(b), and the plain file under exactly that shown name, whose
    // bytes in P differ from b. 1251 and 1252 each give one (0x80/0x88 both ways, measured). Null
    // on a page with no such pair (UTF-8, for one): the tests are then skipped, visibly.
    private static (string Threat, string Shown, Encoding Listing)? SwapPair()
    {
        int system = SystemCodePages.Ansi;
        int other = system == 1252 ? 1251 : 1252;
        Encoding page = SystemCodePages.ForStrict(system);
        Encoding listing = SystemCodePages.For(other);
        for (int b = 0x80; b <= 0xFF; b++)
        {
            try
            {
                string threat = "a" + page.GetString([(byte)b]);
                string shown = "a" + listing.GetString([(byte)b]);
                if (threat != shown && page.GetBytes(shown) is [(byte)'a', var plainByte] && plainByte != b)
                    return (threat, shown, listing);
            }
            catch (DecoderFallbackException)
            {
                // undefined in this page: try the next byte
            }
            catch (EncoderFallbackException)
            {
                // the shown name is no name in this page: try the next byte
            }
        }
        return null;
    }

    private SwapFixture Fixture(bool threatTwice = false)
    {
        var (threat, shown, listing) = SwapPair() ?? throw new InvalidOperationException("no swap pair; the attribute skips this test");
        string path = System.IO.Path.Combine(_temp.Path, "swap.tar");
        TarBuilder.WriteTar(path,
        [
            new TarBuilder.Entry { Name = "label", TypeFlag = 'V' },
            TarBuilder.PaxPath(threat),
            new TarBuilder.Entry { Name = "threat", Content = Encoding.ASCII.GetBytes(ThreatText) },
            .. (threatTwice
                ? (TarBuilder.Entry[])[TarBuilder.PaxPath(threat), new TarBuilder.Entry { Name = "copy", Content = Encoding.ASCII.GetBytes("second copy") }]
                : []),
            TarBuilder.PaxPath(shown),
            new TarBuilder.Entry { Name = "plain", Content = Encoding.ASCII.GetBytes("plain") },
            // A third file, so a filter leaving out one name is sent as "-X" (TestSubset: the shorter list).
            new TarBuilder.Entry { Name = "b.txt", Content = Encoding.ASCII.GetBytes("b") },
        ]);
        return new SwapFixture(path, threat, shown, listing);
    }

    private sealed class SwapPairIntegrationAttribute : FactAttribute
    {
        public SwapPairIntegrationAttribute()
        {
            Skip = new IntegrationAttribute().Skip;
            if (Skip is null && SwapPair() is null)
                Skip = $"no byte swaps two names between system page {SystemCodePages.Ansi} and its pair page";
        }
    }
}
