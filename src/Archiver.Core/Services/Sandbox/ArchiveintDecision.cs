namespace Archiver.Core.Services.Sandbox;

/// <summary>T-F470: which backend this process reads tar-family archives with, and why.</summary>
public sealed class ArchiveintDecision
{
    private ArchiveintDecision(string? dllVersion, string reason, HelperSetup? helper)
    {
        DllVersion = dllVersion;
        Cause = reason;
        Helper = helper;
    }

    /// <summary>True when the archive helper reads.</summary>
    public bool UsesHelper => Helper is not null;

    /// <summary>The libarchive version archiveint.dll reported, when the probe ran.</summary>
    public string? DllVersion { get; }

    /// <summary>Why this backend, in English: a log and diagnostics line, not a user message.</summary>
    public string Cause { get; }

    internal HelperSetup? Helper { get; }

    /// <summary>One English line for the log and <c>pakko i</c>.</summary>
    public string Describe() =>
        $"tar-family reader: {(UsesHelper ? "archive helper" : "tar.exe")}; archiveint.dll: {DllVersion ?? "not probed"}; {Cause}";

    internal static ArchiveintDecision ForTarExe(string? dllVersion, string reason) => new ArchiveintDecision(dllVersion, reason, null);

    internal static ArchiveintDecision ForHelper(string dllVersion, HelperSetup helper) => new ArchiveintDecision(dllVersion, "the probe passed", helper);
}
