using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Archiver.Shell;

// T-F208 / T-F268 step 6: operation titles, the operation window's own labels and size units.
// Mirrors ResultMessagesLocalizer.
public static class OperationTextLocalizer
{
    private static readonly ResourceManager Res =
        new("Archiver.Shell.Resources.OperationText", Assembly.GetExecutingAssembly());

    public static string Get(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Template(key), args);

    /// <summary>The unformatted string, for a composite format filled in elsewhere (the operation window).</summary>
    public static string Template(string key) => Res.GetString(key, CultureInfo.CurrentUICulture)!;
}
