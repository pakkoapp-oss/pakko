using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Archiver.Shell;

// T-F192: mirrors ConflictDialogLocalizer's own resx-based approach exactly. The 6 PasswordDialog*
// values are copied verbatim from Archiver.App's Strings/*/Resources.resw (already translated
// across all 37 locales there, T-F190) via a one-off generator script rather than re-translated —
// same reuse this project already did for T-F155's ConflictDialogLocalizer. PasswordDialogShow
// PasswordCheck is new (no WinUI equivalent — PasswordBox has its own built-in reveal button) and
// was translated fresh across all 37 locales — see DECISIONS.md's T-F192 entry.
public static class PasswordDialogLocalizer
{
    private static readonly ResourceManager Res =
        new("Archiver.Shell.Resources.PasswordMessages", Assembly.GetExecutingAssembly());

    public static string Get(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Res.GetString(key, CultureInfo.CurrentUICulture)!, args);
}
