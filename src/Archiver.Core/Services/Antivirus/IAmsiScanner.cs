using Archiver.Core.Models;

namespace Archiver.Core.Services.Antivirus;

/// <summary>
/// T-F146. Internal seam over the real AMSI P/Invoke wrapper (<see cref="AmsiScanner"/>) so
/// AntivirusScanService's orchestration logic (subset filtering, size-cap skip, provider-empty
/// gate, tar per-file-failure handling) can be unit-tested with a hand-rolled fake, without every
/// test depending on this machine's actual registered AV. One instance = one scan operation's
/// worth of related buffer scans (see AmsiScanner's own doc comment for why session reuse
/// matters) — never call ScanBuffer concurrently on the same instance.
/// </summary>
internal interface IAmsiScanner : IDisposable
{
    /// <summary>
    /// Scans exactly <paramref name="length"/> bytes of <paramref name="buffer"/> (which may be
    /// larger than length when rented from a pool). AMSI's own contract never returns a threat
    /// name — only a numeric verdict — so ThreatName is always null; callers should not invent one.
    /// </summary>
    (ThreatVerdict Verdict, string? ThreatName) ScanBuffer(byte[] buffer, int length, string contentName);
}
