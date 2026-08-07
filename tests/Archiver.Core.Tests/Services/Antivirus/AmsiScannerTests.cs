using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services.Antivirus;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Antivirus;

// T-F146 AC #1: real AmsiScanner against the real amsi.dll on this machine, mirroring the probe
// already run during design (docs/DECISIONS.md's T-F146 entry: "EICAR buffer: result=32768
// detected=True | Clean buffer: result=1 detected=False", confirmed non-elevated). This is
// environment-dependent by nature — it exercises whatever AV is actually registered as this
// machine's AMSI provider, same as any real AMSI consumer (PowerShell, a browser, etc.) would.
// The EICAR bytes are generated at runtime, never committed to disk or to this source file as a
// static constant a repo scan could flag — see docs/DECISIONS.md's Phase 0 finding on why an
// EICAR fixture cannot be committed at all (Defender quarantines it on clone/build).
public sealed class AmsiScannerTests
{
    private static string BuildEicarString() =>
        "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    [Fact]
    public void ScanBuffer_EicarTestString_ReturnsThreatDetected()
    {
        using AmsiScanner scanner = new("PakkoTests");
        byte[] eicar = Encoding.ASCII.GetBytes(BuildEicarString());

        var (verdict, _) = scanner.ScanBuffer(eicar, eicar.Length, "eicar-test.txt");

        verdict.Should().Be(ThreatVerdict.ThreatDetected);
    }

    [Fact]
    public void ScanBuffer_CleanBuffer_ReturnsClean()
    {
        using AmsiScanner scanner = new("PakkoTests");
        byte[] clean = Encoding.ASCII.GetBytes("Just an ordinary text file with nothing suspicious in it.");

        var (verdict, _) = scanner.ScanBuffer(clean, clean.Length, "clean.txt");

        verdict.Should().Be(ThreatVerdict.Clean);
    }

    [Fact]
    public void ScanBuffer_MultipleCallsOnSameInstance_BothSucceed()
    {
        // One AMSI session is reused across multiple buffers (matches AntivirusScanService's own
        // usage — one session per whole scan operation, not per entry).
        using AmsiScanner scanner = new("PakkoTests");
        byte[] eicar = Encoding.ASCII.GetBytes(BuildEicarString());
        byte[] clean = Encoding.ASCII.GetBytes("nothing to see here");

        scanner.ScanBuffer(clean, clean.Length, "a.txt").Verdict.Should().Be(ThreatVerdict.Clean);
        scanner.ScanBuffer(eicar, eicar.Length, "b.txt").Verdict.Should().Be(ThreatVerdict.ThreatDetected);
        scanner.ScanBuffer(clean, clean.Length, "c.txt").Verdict.Should().Be(ThreatVerdict.Clean);
    }
}
