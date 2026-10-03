namespace Archiver.Core.Services;

/// <summary>
/// Widens an "apply to all/remaining" answer beyond the single Core call that asked for it.
/// Core's own ConflictResolver/PasswordResolver already remember such an answer, but only for the
/// lifetime of ONE ExtractAsync/TestAsync call. A frontend that makes several calls for one user
/// action — Archiver.Shell's per-archive loop (T-F155/T-F192), Archiver.CLI's zip/tar split through
/// ExtractionRouter (T-F160) — wraps its prompt in one instance of this, created once per user
/// action, so the answer spans every call. Replaced two hand-written copies of this pattern.
/// </summary>
/// <typeparam name="TInfo">What the prompt is asked about (e.g. a conflict or a password prompt).</typeparam>
/// <typeparam name="TDecision">The prompt's answer.</typeparam>
/// <param name="inner">Asks the user.</param>
/// <param name="isSticky">Whether an answer is remembered for the later calls.</param>
/// <param name="whenReused">Applied to a remembered answer each time it is handed out again, so the
/// receiver can tell it from one given for this call (T-F301); null hands it out unchanged.</param>
public sealed class StickyCallback<TInfo, TDecision>(
    Func<TInfo, Task<TDecision>> inner,
    Func<TDecision, bool> isSticky,
    Func<TDecision, TDecision>? whenReused = null)
    where TDecision : class
{
    private TDecision? _sticky;

    /// <summary>Returns the remembered sticky answer, or asks <c>inner</c> and remembers its answer
    /// when <c>isSticky</c> says so. Pass this method as the Core callback.</summary>
    public async Task<TDecision> ResolveAsync(TInfo info)
    {
        if (_sticky is { } sticky)
            return whenReused is null ? sticky : whenReused(sticky);

        TDecision decision = await inner(info).ConfigureAwait(false);
        if (isSticky(decision))
            _sticky = decision;

        return decision;
    }
}
