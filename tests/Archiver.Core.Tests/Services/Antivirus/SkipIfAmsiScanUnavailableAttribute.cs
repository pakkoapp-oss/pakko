using System;
using Archiver.Core.Models;
using Archiver.Core.Services.Antivirus;

namespace Archiver.Core.Tests.Services.Antivirus;

/// <summary>
/// Marks a test as requiring a real, working AMSI provider on this machine. A registry-based
/// check (<see cref="AmsiProviderCheck"/>) isn't enough here — on some environments (confirmed on
/// GitHub Actions' windows-2022 runner) a provider is registered and <c>AmsiInitialize</c>/
/// <c>AmsiOpenSession</c> both succeed, but <c>AmsiScanBuffer</c> itself fails with
/// <c>ERROR_NOT_READY</c> (HRESULT 0x80070015). This does a real probe scan instead.
/// </summary>
/// <remarks>
/// T-F177 follow-up (test-coverage audit, 2026-08-31): the probe originally scanned an innocuous
/// buffer and only checked that <c>ScanBuffer</c> didn't throw — that proves the provider is
/// reachable, not that it still actually detects anything. A live-but-degraded provider (observed
/// on this dev machine mid-session, after several real EICAR detections/quarantines in a short
/// window — see `docs/TASKS.md`'s T-F177 entry) passed that probe yet silently returned `Clean`
/// for real EICAR, turning every test gated on this attribute into a false pass instead of a
/// clean skip. Now probes with the real EICAR string and requires an actual `ThreatDetected`
/// verdict, matching the identical fix applied to
/// `Archiver.Core.IntegrationTests`' own `SkipIfTarOrAmsiUnavailableAttribute` in the same task.
/// </remarks>
public sealed class SkipIfAmsiScanUnavailableAttribute : FactAttribute
{
    public SkipIfAmsiScanUnavailableAttribute()
    {
        try
        {
            using AmsiScanner scanner = new("PakkoTests");
            byte[] eicar = System.Text.Encoding.ASCII.GetBytes(
                "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
            var (verdict, _) = scanner.ScanBuffer(eicar, eicar.Length, "probe-eicar.txt");
            if (verdict != ThreatVerdict.ThreatDetected)
                Skip = $"AMSI is live but not currently detecting EICAR on this machine (probe verdict: {verdict})";
        }
        catch (InvalidOperationException ex)
        {
            Skip = $"AMSI scanning is not available on this machine: {ex.Message}";
        }
    }
}
