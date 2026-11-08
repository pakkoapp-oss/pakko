using System.Text;

namespace Archiver.Core.Text;

/// <summary>
/// T-F467: the one provider of text encodings. Each member is a named boundary with its measured
/// source; a page is chosen here and nowhere else (<c>TextBoundaryOwnersTests</c>). The standard for
/// every boundary (<c>docs/DECISIONS.md</c>, "T-F467 done"):
/// <list type="number">
/// <item>The page comes from a handler here, built from one <see cref="MachinePages"/> snapshot.</item>
/// <item>A name that decides something goes back to another program only as the bytes that program
/// printed for it (T-F465's guard in <c>TarSandboxScope</c>).</item>
/// <item>A boundary that decides reads <see cref="TextBoundary.Strict"/>, or guards what
/// <see cref="TextBoundary.Lenient"/> read.</item>
/// </list>
/// The Shell's stdin path list has no encoding: it is read as raw UTF-16 code units, so an unpaired
/// surrogate survives (<c>StdinPathList</c>).
/// </summary>
public static class TextBoundaries
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// tar.exe's stdout: the system ANSI page, UTF-8 under the "Beta: Unicode UTF-8" setting. The UCRT
    /// names its locale "&lt;format&gt;.&lt;system ANSI page&gt;" (T-F465, measured on 3.8.9 and 3.5.2).
    /// </summary>
    internal static TextBoundary TarExeOutput(MachinePages machine) => new(machine.Ansi);

    /// <summary>tar.exe's command line and "-T"/"-X" lists: the system ANSI page (T-F266, T-F283).</summary>
    internal static TextBoundary TarExeCommandLine(MachinePages machine) => new(machine.Ansi);

    /// <summary>tar.exe's stderr: the page of its stdout.</summary>
    internal static TextBoundary TarExeMessages(MachinePages machine) => TarExeOutput(machine);

    /// <summary>
    /// The raw name bytes of a tar with no charset, as tar.exe reads them: libarchive's OEM page
    /// (<see cref="LibarchiveOemPage"/>).
    /// </summary>
    internal static TextBoundary TarRawHeaderNames(MachinePages machine) => new(LibarchiveOemPage(machine));

    /// <summary>T-F470: the archive helper's first "--hdrcharset" for names stored in no charset.</summary>
    internal const string ArchiveHelperUtf8Names = "UTF-8";

    /// <summary>
    /// T-F470: the archive helper's "--hdrcharset" for raw names UTF-8 did not read - the page
    /// tar.exe reads them in (<see cref="TarRawHeaderNames"/>), as libarchive names it; null when that
    /// page is UTF-8 itself.
    /// </summary>
    internal static (string Charset, TextBoundary Boundary)? ArchiveHelperRawNames(MachinePages machine)
    {
        TextBoundary raw = TarRawHeaderNames(machine);
        return raw.CodePage == Utf8NoBom.CodePage ? null : ("CP" + raw.CodePage.ToString(System.Globalization.CultureInfo.InvariantCulture), raw);
    }

    /// <summary>A ZIP name without the UTF-8 flag: OEM from FAT/NTFS hosts, ANSI from others (T-F234, 7-Zip's rule).</summary>
    internal static (TextBoundary Oem, TextBoundary Ansi) ZipLegacyNames(MachinePages machine) => (new(machine.Oem), new(machine.Ansi));

    /// <summary>
    /// The CLI's console output for a <c>-scc</c> value: 0 (CP_ACP) the system ANSI page, 1 (CP_OEMCP)
    /// the system OEM page, any other value that page (T-F238).
    /// </summary>
    public static TextBoundary Console(int charset) => Console(charset, MachinePages.Current);

    internal static TextBoundary Console(int charset, MachinePages machine) => charset switch
    {
        0 => new(machine.Ansi),
        1 => new(machine.Oem),
        _ => new(charset),
    };

    /// <summary>A PAR2 file description's name: UTF-8 (what par2cmdline writes and reads).</summary>
    internal static Encoding Par2FileNames => Utf8NoBom;

    /// <summary>The App's activation payload of file paths: UTF-8 inside base64 (<c>LaunchArguments</c>).</summary>
    internal static Encoding LaunchArgumentPayload => Utf8NoBom;

    /// <summary>
    /// libarchive's <c>get_current_oemcp</c> (archive_string.c, the same in v3.5.2 and v3.8.9):
    /// the first <c>acp_ocp_map</c> row whose locale starts with the UCRT locale name (its
    /// <c>strncmp</c> over the name's length), otherwise the system OEM page. Measured in Pakko's
    /// sandbox on 3.5.2 for 12 formats (docs/DECISIONS.md, "T-F467 done").
    /// </summary>
    internal static int LibarchiveOemPage(MachinePages machine)
    {
        if (string.IsNullOrEmpty(machine.UcrtLocaleName))
            return machine.Oem;
        foreach ((string locale, int oem) in AcpOcpMap)
        {
            if (locale.StartsWith(machine.UcrtLocaleName, StringComparison.Ordinal))
                return oem;
        }
        return machine.Oem;
    }

    // libarchive's acp_ocp_map (archive_string.c, v3.5.2 and v3.8.9 identical), in table order,
    // repeats included: (locale, OEM page). The ANSI column is not read by get_current_oemcp.
    private static readonly (string Locale, int Oem)[] AcpOcpMap =
    [
        ("Chinese_Taiwan", 950),
        ("Chinese_People's Republic of China", 936),
        ("Chinese_Taiwan", 950),
        ("Czech_Czech Republic", 852),
        ("Danish_Denmark", 850),
        ("Dutch_Netherlands", 850),
        ("Dutch_Belgium", 850),
        ("English_United States", 437),
        ("English_Australia", 850),
        ("English_Canada", 850),
        ("English_New Zealand", 850),
        ("English_United Kingdom", 850),
        ("English_United States", 437),
        ("Finnish_Finland", 850),
        ("French_France", 850),
        ("French_Belgium", 850),
        ("French_Canada", 850),
        ("French_Switzerland", 850),
        ("German_Germany", 850),
        ("German_Austria", 850),
        ("German_Switzerland", 850),
        ("Greek_Greece", 737),
        ("Hungarian_Hungary", 852),
        ("Icelandic_Iceland", 850),
        ("Italian_Italy", 850),
        ("Italian_Switzerland", 850),
        ("Japanese_Japan", 932),
        ("Korean_Korea", 949),
        ("Norwegian (BokmOl)_Norway", 850),
        ("Norwegian (BokmOl)_Norway", 850),
        ("Norwegian-Nynorsk_Norway", 850),
        ("Polish_Poland", 852),
        ("Portuguese_Portugal", 850),
        ("Portuguese_Brazil", 850),
        ("Russian_Russia", 866),
        ("Slovak_Slovakia", 852),
        ("Spanish_Spain", 850),
        ("Spanish_Mexico", 850),
        ("Spanish_Spain", 850),
        ("Swedish_Sweden", 850),
        ("Turkish_Turkey", 857),
    ];
}
