using System.Globalization;
using Archiver.Messages;
using Microsoft.Win32;

namespace Archiver.Shell;

/// <summary>
/// The language Explorer's commands speak (T-F330). Windows picks a packaged app's own strings
/// (the main window, the file-type names) from the user's language list, not from the display
/// language, and the two can differ; reading only <see cref="CultureInfo.CurrentUICulture"/> made
/// the operation window English next to a Ukrainian main window.
/// </summary>
internal static class ShellUiLanguage
{
    /// <summary>The user's language list in order of preference; empty when Windows has none recorded.</summary>
    public static string[] ReadUserLanguages()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International\User Profile");
        return key?.GetValue("Languages") as string[] ?? [];
    }

    /// <summary>
    /// The first listed language Pakko ships (English when English comes first or none is shipped);
    /// with no list, the shipped culture for <paramref name="display"/> (T-F254).
    /// </summary>
    public static CultureInfo Pick(IReadOnlyList<string> userLanguages, CultureInfo display)
    {
        if (userLanguages.Count == 0)
            return UiCulture.ResolveName(display.Name) is { } shipped ? CultureInfo.GetCultureInfo(shipped) : display;

        CultureInfo listed = UiCulture.ResolveFirst(userLanguages);
        return listed.Equals(CultureInfo.InvariantCulture) ? CultureInfo.GetCultureInfo("en-US") : listed;
    }
}
