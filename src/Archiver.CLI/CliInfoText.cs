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

    /// <summary>T-F403: the 7z methods this system's tar.exe can write.</summary>
    public static string SevenZipMethodsLine(IReadOnlyList<Archiver.Core.Models.SevenZipMethod> methods) =>
        "  methods for 7z: " + string.Join(", ", methods.Select(Archiver.Core.Services.ArchiveFormatPolicy.SevenZipMethodName));
}
