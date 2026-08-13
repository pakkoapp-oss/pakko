namespace Archiver.Core.Models;

/// <summary>
/// Controls Mark-of-the-Web propagation on extracted files (T-F51's EnforceMOTW policy).
/// Numeric values match the HKLM\Software\Policies\Pakko\EnforceMOTW DWORD, mirroring NanaZip's
/// own WriteZoneIdExtract shape.
/// </summary>
public enum MotwMode
{
    /// <summary>Never propagate Zone.Identifier to extracted files.</summary>
    Disabled = 0,

    /// <summary>Propagate Zone.Identifier to every extracted file (today's shipped default).</summary>
    AllFiles = 1,

    /// <summary>Propagate Zone.Identifier only to extensions with a known execution risk.</summary>
    UnsafeExtensionsOnly = 2,
}
