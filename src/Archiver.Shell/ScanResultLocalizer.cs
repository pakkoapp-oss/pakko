using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Archiver.Shell;

// T-F146: mirrors HashResultLocalizer's own small resx-based approach exactly (a separate resx
// base name rather than adding Scan keys into HashMessages.resx -- that file's class/name is
// scoped to Hash, and Scan is a genuinely separate feature with its own locale content).
public static class ScanResultLocalizer
{
    private static readonly ResourceManager Res =
        new("Archiver.Shell.Resources.ScanMessages", Assembly.GetExecutingAssembly());

    public static string Get(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Res.GetString(key, CultureInfo.CurrentUICulture)!, args);
}
