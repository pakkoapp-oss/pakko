using System.Runtime.InteropServices;
using Archiver.Core.Models;

namespace Archiver.Core.Services.Antivirus;

/// <summary>
/// T-F146. Real AMSI-backed scanner (amsi.dll) — <see cref="NativeMethods.AmsiInitialize"/>/
/// <see cref="NativeMethods.AmsiOpenSession"/> run once in the constructor and are reused across
/// every <see cref="ScanBuffer"/> call for this instance's lifetime (matches AMSI's own "a
/// session groups related scans" semantics), then torn down via Dispose
/// (<see cref="NativeMethods.AmsiCloseSession"/> then <see cref="NativeMethods.AmsiUninitialize"/>,
/// in that order — mirrors
/// Services/Sandbox/AppContainerProfile.cs's own P/Invoke pattern). Confirmed empirically
/// (docs/DECISIONS.md's T-F146 entry) to work non-elevated: a real EICAR buffer returns
/// AMSI_RESULT_DETECTED (32768) and a clean buffer returns AMSI_RESULT_NOT_DETECTED (1) on this
/// dev machine with no admin prompt. Never call ScanBuffer concurrently on the same instance —
/// AMSI session thread-safety is undocumented; AntivirusScanService only ever calls this
/// sequentially, one buffer at a time.
/// </summary>
// Deliberately not [SupportedOSPlatform("windows")] — matches Services/Sandbox/'s own convention
// of leaving raw P/Invoke wrapper classes unannotated (AppContainerProfile, etc.); only BCL APIs
// that are themselves platform-annotated (e.g. Microsoft.Win32.Registry, see AmsiProviderCheck)
// need the attribute here, since that's where CA1416 actually originates.
internal sealed class AmsiScanner : IAmsiScanner
{
    // AMSI_RESULT_DETECTED per the Windows SDK's amsi.h — everything below this is "not detected"
    // (AMSI_RESULT_CLEAN = 0, AMSI_RESULT_NOT_DETECTED = 1, and the
    // AMSI_RESULT_BLOCKED_BY_ADMIN_START..END range 16384-20479 all read as Clean here; Pakko has
    // no admin-policy UI of its own to distinguish "blocked by policy" from "genuinely clean").
    private const int AmsiResultDetectedThreshold = 32768;

    private IntPtr _context;
    private IntPtr _session;
    private bool _disposed;

    public AmsiScanner(string appName)
    {
        int hr = NativeMethods.AmsiInitialize(appName, out _context);
        if (hr < 0)
            throw new InvalidOperationException($"AmsiInitialize failed (HRESULT 0x{hr:X8}).");

        hr = NativeMethods.AmsiOpenSession(_context, out _session);
        if (hr < 0)
        {
            NativeMethods.AmsiUninitialize(_context);
            _context = IntPtr.Zero;
            throw new InvalidOperationException($"AmsiOpenSession failed (HRESULT 0x{hr:X8}).");
        }
    }

    public (ThreatVerdict Verdict, string? ThreatName) ScanBuffer(byte[] buffer, int length, string contentName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // GCHandle pinning (not `fixed`/unsafe) — this project doesn't otherwise enable
        // AllowUnsafeBlocks, and a single pin per scanned entry is not a hot path worth the extra
        // surface. Mirrors how other P/Invoke call sites in this codebase avoid unsafe blocks.
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            int hr = NativeMethods.AmsiScanBuffer(
                _context, handle.AddrOfPinnedObject(), (uint)length, contentName, _session, out int result);
            if (hr < 0)
                throw new InvalidOperationException($"AmsiScanBuffer failed (HRESULT 0x{hr:X8}).");

            // AMSI's own contract is a numeric verdict only — it never returns a threat name (that
            // level of detail is provider-specific logging, e.g. Get-MpThreatDetection for
            // Defender, not part of the AMSI API surface itself), so ThreatName is always null here.
            return result >= AmsiResultDetectedThreshold
                ? (ThreatVerdict.ThreatDetected, null)
                : (ThreatVerdict.Clean, null);
        }
        finally
        {
            handle.Free();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_session != IntPtr.Zero)
        {
            NativeMethods.AmsiCloseSession(_context, _session);
            _session = IntPtr.Zero;
        }
        if (_context != IntPtr.Zero)
        {
            NativeMethods.AmsiUninitialize(_context);
            _context = IntPtr.Zero;
        }
    }

    private static class NativeMethods
    {
        [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
        public static extern int AmsiInitialize(string appName, out IntPtr amsiContext);

        [DllImport("amsi.dll")]
        public static extern void AmsiUninitialize(IntPtr amsiContext);

        [DllImport("amsi.dll")]
        public static extern int AmsiOpenSession(IntPtr amsiContext, out IntPtr amsiSession);

        [DllImport("amsi.dll")]
        public static extern void AmsiCloseSession(IntPtr amsiContext, IntPtr amsiSession);

        [DllImport("amsi.dll", CharSet = CharSet.Unicode)]
        public static extern int AmsiScanBuffer(
            IntPtr amsiContext, IntPtr buffer, uint length, string contentName, IntPtr amsiSession, out int result);
    }
}
