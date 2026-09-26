using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Archiver.Shell;

// T-F163: the operation-result text (now built by OperationMessages, T-F268) was hardcoded
// English regardless of CurrentUICulture -- everything else in Archiver.Shell (HashMessages,
// ScanMessages, ConflictMessages) already went through this exact pattern. Mirrors
// HashResultLocalizer.
public static class ResultMessagesLocalizer
{
    private static readonly ResourceManager Res =
        new("Archiver.Shell.Resources.ResultMessages", Assembly.GetExecutingAssembly());

    public static string Get(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Res.GetString(key, CultureInfo.CurrentUICulture)!, args);
}
