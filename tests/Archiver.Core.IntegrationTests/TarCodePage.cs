using System.Runtime.InteropServices;

namespace Archiver.Core.IntegrationTests;

// The ANSI code page tar.exe reads its command line through — tests about lossy names only
// apply where it is not UTF-8.
internal static class TarCodePage
{
    public static uint Ansi => GetACP();

    public static bool IsUtf8Ansi => Ansi == 65001;

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();
}
