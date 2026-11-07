using System.Diagnostics;
using System.Text;
using Archiver.Core.Text;

namespace Archiver.Core.IntegrationTests;

// The code pages tar.exe uses on this machine (T-F467's handlers): the system ANSI page for its
// command line and the names it prints, and libarchive's OEM page for tar headers without a charset.
internal static class TarCodePage
{
    public static uint Ansi => (uint)TextBoundaries.TarExeCommandLine(MachinePages.Current).CodePage;

    public static bool IsUtf8Ansi => Ansi == 65001;

    // For failure messages: other Windows builds ship other bsdtar versions and locales.
    public static string Describe()
    {
        string version;
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(@"C:\Windows\System32\tar.exe", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using Process process = System.Diagnostics.Process.Start(start)!;
            version = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            version = ex.Message;
        }
        return $"[{version}; ACP {Ansi}; raw-name OEM {RawNameOem}; UCRT {MachinePages.Current.UcrtLocaleName}]";
    }

    public static int RawNameOem => TextBoundaries.TarRawHeaderNames(MachinePages.Current).CodePage;

    // A name tar.exe can show on this machine and that a legacy tool could have written in its
    // OEM page — Cyrillic on a uk/ru machine, Latin-1 on an en-US one.
    public static string? PortableNonAsciiName()
    {
        foreach (string candidate in new[] { "Док.txt", "café.txt" })
        {
            if (RoundTrips(candidate, RawNameOem) && RoundTrips(candidate, (int)Ansi))
                return candidate;
        }
        return null;
    }

    public static bool RoundTrips(string value, int codePage)
    {
        if (codePage == 65001)
            return true;
        Encoding encoding = Encoding(codePage);
        try { return encoding.GetString(encoding.GetBytes(value)) == value; }
        catch (EncoderFallbackException) { return false; }
    }

    public static Encoding Encoding(int codePage) => new TextBoundary(codePage).Strict;
}
