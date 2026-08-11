using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Archiver.Shell;

// T-F155: mirrors ScanResultLocalizer/HashResultLocalizer's own resx-based approach exactly. The
// 6 ConflictDialog* values are copied verbatim from Archiver.App's Strings/*/Resources.resw
// (already translated across all 37 locales there, T-F06) via a one-off generator script rather
// than re-translated -- see DECISIONS.md's T-F155 entry.
public static class ConflictDialogLocalizer
{
    private static readonly ResourceManager Res =
        new("Archiver.Shell.Resources.ConflictMessages", Assembly.GetExecutingAssembly());

    public static string Get(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Res.GetString(key, CultureInfo.CurrentUICulture)!, args);
}
