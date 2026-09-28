using Archiver.Core.Models;

namespace Archiver.App.Core;

/// <summary>
/// T-F200: the Archive Browser remembers a password that worked for an archive, so previewing a
/// second file, drilling into a nested archive or extracting again does not ask again. Held in
/// memory for the browse session only: cleared when another archive is opened, when browsing
/// ends and when the window closes; never written anywhere.
/// </summary>
public sealed class SessionPasswordMemory
{
    private readonly Dictionary<string, string> _verified = new(StringComparer.OrdinalIgnoreCase);
    private string? _pendingArchive;
    private string? _pending;

    /// <summary>
    /// Wraps <paramref name="prompt"/> for one operation on <paramref name="archivePath"/>: the
    /// first attempt gets the remembered password without a prompt; a rejected one is forgotten
    /// and the user is asked.
    /// </summary>
    public Func<PasswordPromptInfo, Task<PasswordDecision>> Wrap(
        string archivePath, Func<PasswordPromptInfo, Task<PasswordDecision>> prompt)
    {
        _pendingArchive = archivePath;
        _pending = null;
        return async info =>
        {
            if (info.PreviousAttemptWasWrong)
                _verified.Remove(archivePath);
            else if (_verified.TryGetValue(archivePath, out string? remembered))
                return new PasswordDecision { Password = remembered };

            PasswordDecision decision = await prompt(info).ConfigureAwait(true);
            _pending = decision.Password;
            return decision;
        };
    }

    /// <summary>
    /// Ends the operation <see cref="Wrap"/> started: a password the result did not reject is
    /// remembered for <paramref name="archivePath"/>; a rejected one is forgotten.
    /// </summary>
    public void Complete(string archivePath, ArchiveResult result)
    {
        if (!string.Equals(_pendingArchive, archivePath, StringComparison.OrdinalIgnoreCase))
            return;
        if (WasPasswordRejected(result))
            _verified.Remove(archivePath);
        else if (_pending is { } password)
            _verified[archivePath] = password;
        _pending = null;
        _pendingArchive = null;
    }

    /// <summary>Forgets every password.</summary>
    public void Clear()
    {
        _verified.Clear();
        _pending = null;
        _pendingArchive = null;
    }

    /// <summary>True when the result reports a missing or wrong password.</summary>
    public static bool WasPasswordRejected(ArchiveResult result) =>
        result.Errors.Any(e => IsPasswordCode(e.Text?.Code))
        || result.SkippedFiles.Any(s => IsPasswordCode(s.Text?.Code));

    private static bool IsPasswordCode(MessageCode? code) => code is MessageCode.PasswordProtectedExtract
        or MessageCode.PasswordProtectedTest or MessageCode.PasswordProtectedBrowse or MessageCode.EntryWrongPassword;
}
