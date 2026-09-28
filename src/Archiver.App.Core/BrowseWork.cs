namespace Archiver.App.Core;

/// <summary>
/// T-F199 step 6: counts browse work that runs without <c>IsBusy</c> (listing, nested drill-in,
/// preview). "Close archive" stays off while any is in flight, or the work would finish into a
/// closed browser (fill the index, push a nested level) after the user left.
/// </summary>
public sealed class BrowseWork
{
    private int _count;

    /// <summary>Raised when <see cref="InFlight"/> flips.</summary>
    public event Action? Changed;

    /// <summary>True while at least one scope is open.</summary>
    public bool InFlight => _count > 0;

    /// <summary>Starts a unit of work; dispose the result when it ends.</summary>
    public IDisposable Begin()
    {
        if (++_count == 1)
            Changed?.Invoke();
        return new Scope(this);
    }

    private void End()
    {
        if (--_count == 0)
            Changed?.Invoke();
    }

    private sealed class Scope(BrowseWork owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.End();
        }
    }
}
