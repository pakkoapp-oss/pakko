using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Archiver.Shell;

// T-F128 follow-up: the first localized text in Archiver.Shell (previously "no localization
// anywhere in Archiver.Shell's own text" per CLAUDE.md). Plain .resx satellite-assembly
// localization, not Archiver.App's WinRT ResourceLoader/.resw — resw needs a Windows-versioned
// TFM for compile-time WinRT projections, which caused a real stale-build-path bug the one time
// it was tried here for an unrelated feature (see DECISIONS.md's T-F128 toast entry). .resx needs
// no TFM change and is a more natural fit for a non-XAML console-style WinExe anyway.
public static class HashResultLocalizer
{
    private static readonly ResourceManager Res =
        new("Archiver.Shell.Resources.HashMessages", Assembly.GetExecutingAssembly());

    public static string Get(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Res.GetString(key, CultureInfo.CurrentUICulture)!, args);
}
