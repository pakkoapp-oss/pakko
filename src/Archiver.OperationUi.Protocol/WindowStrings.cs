namespace Archiver.OperationUi.Protocol;

/// <summary>
/// Keys of <see cref="Hello.Strings"/>. The helper has no resources of its own: Shell sends every
/// label, localized, and a missing key renders as the key itself.
/// </summary>
public static class WindowStrings
{
    public const string Cancel = "Cancel";

    /// <summary>The Cancel button when the command covers several archives.</summary>
    public const string CancelAll = "CancelAll";

    public const string Close = "Close";

    /// <summary>Composite format: {0} index, {1} count, {2} archive name.</summary>
    public const string ItemOfCount = "ItemOfCount";

    public const string ConflictTitle = "ConflictTitle";

    public const string ExistingFile = "ExistingFile";

    public const string IncomingFile = "IncomingFile";

    /// <summary>Appended to the incoming file's details when it is the newer one.</summary>
    public const string Newer = "Newer";

    public const string Overwrite = "Overwrite";

    public const string Rename = "Rename";

    public const string Skip = "Skip";

    public const string ApplyToAll = "ApplyToAll";

    public const string PasswordTitle = "PasswordTitle";

    /// <summary>Composite format: {0} archive name.</summary>
    public const string PasswordMessage = "PasswordMessage";

    public const string PasswordLabel = "PasswordLabel";

    public const string WrongPassword = "WrongPassword";

    public const string ApplyToRemaining = "ApplyToRemaining";

    public const string PasswordOk = "PasswordOk";

    /// <summary>Declines the password: this archive is not opened, the others go on.</summary>
    public const string SkipArchive = "SkipArchive";
}
