namespace Archiver.Core.Models;

/// <summary>
/// Controls Mark-of-the-Web propagation on extracted files (T-F51's EnforceMOTW policy).
/// Numeric values match the HKLM\Software\Policies\Pakko\EnforceMOTW DWORD, mirroring NanaZip's
/// own WriteZoneIdExtract shape.
/// </summary>
public enum MotwMode
{
    Disabled = 0,
    AllFiles = 1,
    UnsafeExtensionsOnly = 2,
}
