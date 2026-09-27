using System.Globalization;
using Archiver.Core.Models;
using Archiver.OperationUi.Protocol;

namespace Archiver.Shell;

/// <summary>
/// The labels Shell sends the operation window helper. The prompts reuse the Win32 dialogs'
/// translated strings; the window's own labels are in OperationText.resx.
/// </summary>
internal static class OperationWindowText
{
    // ZIP stores times in 2-second steps, so a closer pair is the same time, not a newer file.
    private static readonly TimeSpan NewerThreshold = TimeSpan.FromSeconds(2);

    public static Hello CreateHello()
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        return new Hello(FrameCodec.ProtocolVersion, culture.Name, culture.TextInfo.IsRightToLeft, new Dictionary<string, string>
        {
            [WindowStrings.Cancel] = PasswordDialogLocalizer.Get("PasswordDialogCancelButton"),
            [WindowStrings.CancelAll] = OperationTextLocalizer.Get("WindowCancelAll"),
            [WindowStrings.Close] = OperationTextLocalizer.Get("WindowClose"),
            [WindowStrings.ItemOfCount] = OperationTextLocalizer.Template("WindowItemOfCount"),
            [WindowStrings.ConflictTitle] = ConflictDialogLocalizer.Get("ConflictDialogTitle"),
            [WindowStrings.ExistingFile] = OperationTextLocalizer.Get("WindowExistingFile"),
            [WindowStrings.IncomingFile] = OperationTextLocalizer.Get("WindowIncomingFile"),
            [WindowStrings.Newer] = OperationTextLocalizer.Get("WindowNewer"),
            [WindowStrings.Overwrite] = ConflictDialogLocalizer.Get("ConflictDialogOverwriteButton"),
            [WindowStrings.Rename] = ConflictDialogLocalizer.Get("ConflictDialogRenameButton"),
            [WindowStrings.Skip] = ConflictDialogLocalizer.Get("ConflictDialogSkipButton"),
            [WindowStrings.ApplyToAll] = ConflictDialogLocalizer.Get("ConflictDialogApplyToAllCheck"),
            [WindowStrings.PasswordTitle] = PasswordDialogLocalizer.Get("PasswordDialogTitle"),
            [WindowStrings.PasswordMessage] = PasswordDialogLocalizer.Template("PasswordDialogMessage"),
            [WindowStrings.PasswordLabel] = OperationTextLocalizer.Get("WindowPasswordLabel"),
            [WindowStrings.WrongPassword] = PasswordDialogLocalizer.Get("PasswordDialogWrongPasswordHint"),
            [WindowStrings.ApplyToRemaining] = PasswordDialogLocalizer.Get("PasswordDialogApplyToRemainingCheck"),
            [WindowStrings.PasswordOk] = PasswordDialogLocalizer.Get("PasswordDialogOkButton"),
            [WindowStrings.SkipArchive] = OperationTextLocalizer.Get("WindowSkipArchive"),
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
            ? OperationTextLocalizer.Get("WindowModified", m.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
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
