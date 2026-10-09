namespace Archiver.Core.Recovery;

/// <summary>Turns units of work done into a fraction of <paramref name="total"/> (T-F275).</summary>
internal sealed class Par2Progress(Action<double>? report, double total)
{
    private double _done;

    internal void Add(double units)
    {
        if (report is null)
            return;
        _done += units;
        report(total <= 0 ? 1.0 : Math.Min(1.0, _done / total));
    }
}
