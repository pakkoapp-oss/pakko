using Archiver.Core.Models;

namespace Archiver.Core.Services;

// T-F189: resolves an encrypted archive's password via the caller's ResolvePasswordAsync
// callback, retrying up to maxAttempts times and remembering an "apply to remaining" choice for
// the rest of this instance's lifetime — same shape as ConflictResolver (one instance per
// ArchiveAsync/ExtractAsync call). Shared by both directions (Decrypt today, Encrypt from T-F193
// onward) per the plan's "one mechanism, not two copies" decision — maxAttempts is a parameter
// from each call site (ExtractAsync passes 3, a future ArchiveAsync passes 1) rather than a
// constant baked in here, so there is no Purpose-branch inside the retry loop itself. verify is
// caller-supplied so this class stays format-agnostic — it knows nothing about ZIP/AES/ZipCrypto.
internal sealed class PasswordResolver(
    Func<PasswordPromptInfo, Task<PasswordDecision>>? resolvePasswordAsync,
    int maxAttempts)
{
    private string? _sticky;

    /// <summary>
    /// Returns the resolved password, or null when the user cancelled, no resolver is wired, or
    /// every attempt was rejected by <paramref name="verify"/> — every null case maps to today's
    /// unchanged "password-protected" rejection message at the call site.
    /// </summary>
    public async Task<string?> ResolveAsync(
        string archiveName, PasswordPurpose purpose, Func<string, bool> verify)
    {
        // Deliberate, mirrors ConflictResolver's identical _sticky short-circuit: once
        // ApplyToRemaining is set, later archives skip verify() entirely, trusting the earlier
        // success. A wrong sticky password against a later archive's differently-keyed entries
        // therefore surfaces as per-entry "wrong password" ArchiveErrors in the caller's normal
        // extraction loop (and a "no entries extracted" skip for that archive), not a re-prompt —
        // same non-retry contract "apply to all" already has for conflicts.
        if (_sticky is { } sticky)
            return sticky;

        if (resolvePasswordAsync is null)
            return null;

        bool previousAttemptWasWrong = false;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var decision = await resolvePasswordAsync(new PasswordPromptInfo
            {
                ArchiveName = archiveName,
                Purpose = purpose,
                AttemptNumber = attempt,
                PreviousAttemptWasWrong = previousAttemptWasWrong,
            }).ConfigureAwait(false);

            if (decision.Password is null)
                return null; // user cancelled

            if (verify(decision.Password))
            {
                if (decision.ApplyToRemaining)
                    _sticky = decision.Password;
                return decision.Password;
            }

            previousAttemptWasWrong = true;
        }

        return null; // attempts exhausted
    }
}
