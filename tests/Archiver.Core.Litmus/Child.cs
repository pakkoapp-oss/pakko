using System.Diagnostics;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Core.Litmus;

internal static class Child
{
    public static async Task<int> RunAsync(string opName, string shapeName, int scale)
    {
        if (await Console.In.ReadLineAsync() != "go")
            return 2; // the parent puts the process in its job first, then says go
        Operation op = Operations.All.Single(o => o.Name == opName);
        var services = PakkoServices.Create(new GroupPolicyOptions());
        Dictionary<string, Shape> shapes = await Fixtures.PrepareAsync(Fixtures.RootFor(scale), scale, services);

        string? warmSkip = await RunOnceAsync(op, shapes[Fixtures.MiniOf(shapeName)], services, measure: false);
        if (warmSkip is not null)
        {
            await Console.Out.WriteLineAsync("SKIP " + warmSkip);
            return 0;
        }
        string? skip = await RunOnceAsync(op, shapes[shapeName], services, measure: true);
        if (skip is not null)
            await Console.Out.WriteLineAsync("SKIP " + skip);
        return 0;
    }

    private static async Task<string?> RunOnceAsync(Operation op, Shape shape, PakkoServices services, bool measure)
    {
        string output = Path.Combine(Path.GetTempPath(), "pakko-litmus", "out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var context = new RunContext(shape, output, services);
            if (op.Prepare is not null)
                await op.Prepare(context);
            GC.Collect(); // NOSONAR: S1215 - the measured run starts from a collected heap, as T-F498's counters require
            GC.WaitForPendingFinalizers();
            GC.Collect(); // NOSONAR: S1215 - see above

            var before = Snapshot.Take();
            var clock = Stopwatch.StartNew();
            string? skip = await op.Run(context);
            clock.Stop();
            var after = Snapshot.Take();
            if (measure && skip is null)
                await Console.Out.WriteLineAsync("RESULT " + after.Minus(before, clock.Elapsed.TotalMilliseconds));
            return skip;
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); } catch (IOException) { /* best-effort: a scanner may hold a file */ }
        }
    }
}
