using Archiver.Core.Models;

namespace Archiver.CLI;

/// <summary>
/// T-F221: what the result report needs besides the result itself — the staged -si archive (shown
/// as "(stdin)", never its temp path), the archives whose -p the password resolver already called
/// wrong (Core's generic "password-protected" line would contradict it), and which hints apply.
/// </summary>
internal sealed class CliReportContext
{
    public string? StdinPath { get; init; }

    /// <summary>A -p or a bare -p was given, so "use -p" is no help.</summary>
    public bool PasswordGiven { get; init; }

    /// <summary>Existing files were kept because nothing else was chosen (no -ao/-y, no console to ask).</summary>
    public bool KeptExistingByDefault { get; init; }

    /// <summary>Archive file names the fixed -p was already reported wrong for.</summary>
    public HashSet<string> WrongPasswordArchives { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The name an error line shows for <paramref name="path"/>.</summary>
    public string DisplayName(string path) =>
        StdinPath is not null && string.Equals(path, StdinPath, StringComparison.OrdinalIgnoreCase) ? "(stdin)" : Path.GetFileName(path);

    /// <summary>The name a prompt or message shows for an archive given by file name.</summary>
    public string DisplayArchiveName(string archiveName) =>
        StdinPath is not null && string.Equals(archiveName, Path.GetFileName(StdinPath), StringComparison.OrdinalIgnoreCase)
            ? "(stdin)" : archiveName;

    /// <summary>A Core error the CLI has already reported in its own, more specific words.</summary>
    public bool IsAlreadyReported(ArchiveError error) =>
        error.Text?.Code is MessageCode.PasswordProtectedExtract or MessageCode.PasswordProtectedTest
        && WrongPasswordArchives.Contains(Path.GetFileName(error.SourcePath));
}
