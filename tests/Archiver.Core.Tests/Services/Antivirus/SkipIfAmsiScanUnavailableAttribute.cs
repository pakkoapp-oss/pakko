using System;
using Archiver.Core.Services.Antivirus;

namespace Archiver.Core.Tests.Services.Antivirus;

/// <summary>
/// Marks a test as requiring a real, working AMSI provider on this machine. A registry-based
/// check (<see cref="AmsiProviderCheck"/>) isn't enough here — on some environments (confirmed on
/// GitHub Actions' windows-2022 runner) a provider is registered and <c>AmsiInitialize</c>/
/// <c>AmsiOpenSession</c> both succeed, but <c>AmsiScanBuffer</c> itself fails with
/// <c>ERROR_NOT_READY</c> (HRESULT 0x80070015). This does a real probe scan instead.
/// </summary>
public sealed class SkipIfAmsiScanUnavailableAttribute : FactAttribute
{
    public SkipIfAmsiScanUnavailableAttribute()
    {
        try
        {
            using AmsiScanner scanner = new("PakkoTests");
            byte[] probe = "probe"u8.ToArray();
            scanner.ScanBuffer(probe, probe.Length, "probe.txt");
        }
        catch (InvalidOperationException ex)
        {
            Skip = $"AMSI scanning is not available on this machine: {ex.Message}";
        }
    }
}
