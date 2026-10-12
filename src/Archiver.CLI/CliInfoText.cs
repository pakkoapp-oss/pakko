namespace Archiver.CLI;

/// <summary>What 'pakko i' says a format can do.</summary>
public static class CliInfoText
{
    public const string CreateExtractList = "create, extract, list";

    /// <summary>
    /// T-F342: 7z is created only where this system's tar.exe has a 7z writer; reading it does not
    /// depend on that.
    /// </summary>
    public static string SevenZipActions(bool writerAvailable) => writerAvailable ? CreateExtractList : "extract, list";
}
