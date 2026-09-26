namespace Archiver.Shell;

/// <summary>Starts the operation window helper (T-F268). Throws when it cannot be started.</summary>
internal interface IHelperLauncher
{
    HelperConnection Launch();
}

/// <summary>A started helper: the two pipe ends Shell uses, and a way to end the helper.</summary>
internal sealed class HelperConnection(Stream toHelper, Stream fromHelper, Action kill, IDisposable? owner = null) : IDisposable
{
    public Stream ToHelper { get; } = toHelper;

    public Stream FromHelper { get; } = fromHelper;

    /// <summary>Best effort and idempotent. Ending the helper also ends a read blocked on its pipe.</summary>
    public void Kill() => kill();

    public void Dispose()
    {
        ToHelper.Dispose();
        FromHelper.Dispose();
        owner?.Dispose();
    }
}
