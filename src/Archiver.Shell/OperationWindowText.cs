using System.Globalization;
using System.Text;
using Archiver.Core.Models;
using Archiver.OperationUi.Protocol;

namespace Archiver.Shell;

/// <summary>
/// The labels Shell sends the operation window helper. The prompts reuse the Win32 dialogs'
/// translated strings; the rest is English until T-F268 step 6 localizes it.
/// </summary>
internal static class OperationWindowText
{
    // ZIP stores times in 2-second steps, so a closer pair is the same time, not a newer file.
    private static readonly TimeSpan NewerThreshold = TimeSpan.FromSeconds(2);

    private static readonly CompositeFormat ModifiedFormat = CompositeFormat.Parse("modified {0}");

    public static Hello CreateHello()
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        return new Hello(FrameCodec.ProtocolVersion, culture.Name, culture.TextInfo.IsRightToLeft, new Dictionary<string, string>
        {
            [WindowStrings.Cancel] = "Cancel",
            [WindowStrings.CancelAll] = "Cancel all",
            [WindowStrings.Close] = "Close",
            [WindowStrings.ItemOfCount] = "Archive {0} of {1}  ·  {2}",
            [WindowStrings.ConflictTitle] = ConflictDialogLocalizer.Get("ConflictDialogTitle"),
            [WindowStrings.ExistingFile] = "Existing file",
            [WindowStrings.IncomingFile] = "From the archive",
            [WindowStrings.Newer] = "newer",
            [WindowStrings.Overwrite] = ConflictDialogLocalizer.Get("ConflictDialogOverwriteButton"),
            [WindowStrings.Rename] = ConflictDialogLocalizer.Get("ConflictDialogRenameButton"),
            [WindowStrings.Skip] = ConflictDialogLocalizer.Get("ConflictDialogSkipButton"),
            [WindowStrings.ApplyToAll] = ConflictDialogLocalizer.Get("ConflictDialogApplyToAllCheck"),
            [WindowStrings.PasswordTitle] = PasswordDialogLocalizer.Get("PasswordDialogTitle"),
            [WindowStrings.PasswordMessage] = PasswordDialogLocalizer.Template("PasswordDialogMessage"),
            [WindowStrings.PasswordLabel] = "Password",
            [WindowStrings.WrongPassword] = PasswordDialogLocalizer.Get("PasswordDialogWrongPasswordHint"),
            [WindowStrings.ApplyToRemaining] = PasswordDialogLocalizer.Get("PasswordDialogApplyToRemainingCheck"),
            [WindowStrings.PasswordOk] = PasswordDialogLocalizer.Get("PasswordDialogOkButton"),
            [WindowStrings.SkipArchive] = "Skip archive",
        });
    }

    /// <summary>The conflict prompt with both files' details; the existing file is read here, best effort.</summary>
    public static AskConflict CreateAskConflict(int requestId, ConflictInfo info)
    {
        (long? existingSize, DateTimeOffset? existingModified) = DescribeExisting(info.ExistingPath);
        bool incomingIsNewer = existingModified is { } existing && info.IncomingModified is { } incoming
            && incoming.UtcDateTime - existing.UtcDateTime > NewerThreshold;
        return new AskConflict(
            requestId,
            info.ExistingPath,
            Details(existingSize, existingModified),
            Details(info.IncomingSize, info.IncomingModified),
            incomingIsNewer);
    }

    private static (long? Size, DateTimeOffset? Modified) DescribeExisting(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? (file.Length, new DateTimeOffset(file.LastWriteTimeUtc)) : (null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, null);
        }
    }

    private static string? Details(long? size, DateTimeOffset? modified)
    {
        string? date = modified is { } m
            ? string.Format(CultureInfo.CurrentCulture, ModifiedFormat, m.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
            : null;
        return (size, date) switch
        {
            ({ } s, { } d) => ProgressText.FormatBytes(s) + " · " + d,
            ({ } s, null) => ProgressText.FormatBytes(s),
            (null, { } d) => d,
            _ => null,
        };
    }
}
