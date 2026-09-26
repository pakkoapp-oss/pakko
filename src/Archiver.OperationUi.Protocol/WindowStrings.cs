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
}
