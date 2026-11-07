namespace Archiver.Core.Text;

/// <summary>
/// T-F467: what this machine's text boundaries depend on, read once and kept: the system ANSI and
/// OEM pages, and the locale name the C runtime gives <c>setlocale(LC_ALL, "")</c>
/// ("&lt;English language&gt;_&lt;English country&gt;" of the user's regional format, measured on 12
/// formats; null when Windows gives none). Tests pass explicit values, so every handler of
/// <see cref="TextBoundaries"/> runs its matrix on any machine.
/// </summary>
internal sealed record MachinePages(int Ansi, int Oem, string? UcrtLocaleName)
{
    /// <summary>This machine now.</summary>
    public static MachinePages Current => new(SystemCodePages.Ansi, SystemCodePages.Oem, SystemCodePages.UcrtLocaleName);
}
