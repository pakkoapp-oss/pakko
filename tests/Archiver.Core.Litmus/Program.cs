using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Archiver.Core.Litmus;
using Archiver.Core.Models;
using Archiver.Core.Services;

[assembly: SupportedOSPlatform("windows")]

// T-F498: each operation runs in a child process of its own, inside a Job object, so the counters
// see that operation and every process it starts. The child warms the same operation up on a small
// input first and measures only the real run: docs/TESTING.md, "T-F498 litmus run".
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
return args is ["--child", string op, string shape, string scale]
    ? await Child.RunAsync(op, shape, int.Parse(scale, CultureInfo.InvariantCulture))
    : await Parent.RunAsync(args);

internal static class Child
{
    public static async Task<int> RunAsync(string opName, string shapeName, int scale)
    {
        if (Console.In.ReadLine() != "go")
            return 2; // the parent puts the process in its job first, then says go
        Operation op = Operations.All.Single(o => o.Name == opName);
        var services = PakkoServices.Create(new GroupPolicyOptions());
        Dictionary<string, Shape> shapes = await Fixtures.PrepareAsync(Fixtures.RootFor(scale), scale, services);

        string? warmSkip = await RunOnceAsync(op, shapes[Fixtures.MiniOf(shapeName)], services, measure: false);
        if (warmSkip is not null)
        {
            Console.WriteLine("SKIP " + warmSkip);
            return 0;
        }
        string? skip = await RunOnceAsync(op, shapes[shapeName], services, measure: true);
        if (skip is not null)
            Console.WriteLine("SKIP " + skip);
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
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var before = Snapshot.Take();
            var clock = Stopwatch.StartNew();
            string? skip = await op.Run(context);
            clock.Stop();
            var after = Snapshot.Take();
            if (measure && skip is null)
                Console.WriteLine("RESULT " + after.Minus(before, clock.Elapsed.TotalMilliseconds));
            return skip;
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); } catch (IOException) { /* best-effort: a scanner may hold a file */ }
        }
    }
}

internal readonly record struct Snapshot(uint OwnFaults, uint JobFaults, long JobCpuTicks, uint JobProcesses, long Allocated, int Handles, ulong PeakWorkingSet)
{
    public static Snapshot Take()
    {
        ProcessMemoryCounters memory = Native.OwnMemory();
        JobAccounting job = Native.OwnJob();
        using var self = Process.GetCurrentProcess();
        return new Snapshot(memory.PageFaultCount, job.TotalPageFaultCount, job.TotalUserTime + job.TotalKernelTime,
            job.TotalProcesses, GC.GetTotalAllocatedBytes(precise: true), self.HandleCount, memory.PeakWorkingSetSize);
    }

    // Faults of the processes the operation started = the job's - this process's own.
    public string Minus(Snapshot before, double milliseconds)
    {
        // The job's counter can lag this process's by a few faults at the moment of reading.
        long own = (long)OwnFaults - before.OwnFaults;
        long children = Math.Max(0, (long)JobFaults - before.JobFaults - own);
        return string.Create(CultureInfo.InvariantCulture,
            $"ms={milliseconds:F1};own={own};children={children};cpu={(JobCpuTicks - before.JobCpuTicks) / 10_000.0:F1};procs={JobProcesses - before.JobProcesses};alloc={Allocated - before.Allocated};handles={Handles - before.Handles};peak={PeakWorkingSet}");
    }
}

internal sealed record Sample(double Ms, double Own, double Children, double Cpu, double Procs, double Alloc, double Handles, double Peak)
{
    public static Sample Parse(string line)
    {
        var v = line.Split(';').Select(p => p.Split('='))
            .ToDictionary(p => p[0], p => double.Parse(p[1], CultureInfo.InvariantCulture));
        return new Sample(v["ms"], v["own"], v["children"], v["cpu"], v["procs"], v["alloc"], v["handles"], v["peak"]);
    }
}

internal static class Parent
{
    public static async Task<int> RunAsync(string[] args)
    {
        int runs = 3;
        int scale = 1;
        string[]? only = null;
        string[] shapes = Fixtures.Measured;
        string? csv = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--runs": runs = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--scale": scale = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--ops": only = args[++i].Split(','); break;
                case "--shapes": shapes = args[++i].Split(','); break;
                case "--csv": csv = args[++i]; break;
                default:
                    Console.Error.WriteLine($"Unknown argument {args[i]}. Usage: [--runs N] [--scale N] [--ops a,b] [--shapes many,one] [--csv file]");
                    return 2;
            }
        }
        if (only?.FirstOrDefault(name => Operations.All.All(o => o.Name != name)) is { } unknown)
        {
            Console.Error.WriteLine($"Unknown operation {unknown}: {string.Join(", ", Operations.All.Select(o => o.Name))}");
            return 2;
        }

        Console.Error.WriteLine("Preparing fixtures (once per scale)...");
        Dictionary<string, Shape> fixtures = await Fixtures.PrepareAsync(Fixtures.RootFor(scale), scale, PakkoServices.Create(new GroupPolicyOptions()));

        var rows = new List<(Operation Op, string Shape, Sample? Median, Sample? Min, Sample? Max, string? Skip)>();
        foreach (Operation op in Operations.All.Where(o => only is null || only.Contains(o.Name)))
        {
            foreach (string shape in shapes)
            {
                var samples = new List<Sample>();
                string? skip = null;
                for (int run = 0; run < runs && skip is null; run++)
                {
                    (Sample? sample, string? skipped) = await RunChildAsync(op.Name, shape, scale);
                    skip = skipped;
                    if (sample is not null)
                        samples.Add(sample);
                }
                Console.Error.WriteLine($"{op.Name} {shape}: {(skip ?? $"{samples.Count} runs")}");
                rows.Add(skip is not null || samples.Count == 0
                    ? (op, shape, null, null, null, skip ?? "no result")
                    : (op, shape, Median(samples), MinOf(samples), MaxOf(samples), null));
            }
        }

        Report.Print(rows, fixtures, scale, runs);
        if (csv is not null)
            Report.WriteCsv(csv, rows, fixtures);
        return 0;
    }

    private static async Task<(Sample?, string?)> RunChildAsync(string op, string shape, int scale)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (string arg in new[] { "--child", op, shape, scale.ToString(CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(arg);

        using SafeJobHandle job = Native.CreateJobObjectW(0, null);
        if (job.IsInvalid)
            throw new InvalidOperationException("CreateJobObjectW failed.");
        using Process child = Process.Start(start) ?? throw new InvalidOperationException("The child did not start.");
        if (!Native.AssignProcessToJobObject(job, child.SafeHandle))
        {
            child.Kill();
            throw new InvalidOperationException("AssignProcessToJobObject failed.");
        }
        Task<string> stdout = child.StandardOutput.ReadToEndAsync();
        Task<string> stderr = child.StandardError.ReadToEndAsync();
        await child.StandardInput.WriteLineAsync("go");
        child.StandardInput.Close();
        await child.WaitForExitAsync();
        string output = await stdout;
        string errors = await stderr;
        if (child.ExitCode != 0)
            throw new InvalidOperationException($"{op} on {shape} exited with {child.ExitCode}: {errors}{output}");

        string[] lines = output.Split('\n', StringSplitOptions.TrimEntries);
        if (lines.FirstOrDefault(l => l.StartsWith("SKIP ", StringComparison.Ordinal)) is { } skip)
            return (null, skip[5..]);
        string result = lines.FirstOrDefault(l => l.StartsWith("RESULT ", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"{op} on {shape} printed no result: {output}{errors}");
        return (Sample.Parse(result[7..]), null);
    }

    private static Sample Median(List<Sample> s) => Pick(s, list => list.OrderBy(x => x).ElementAt(list.Count / 2));
    private static Sample MinOf(List<Sample> s) => Pick(s, list => list.Min());
    private static Sample MaxOf(List<Sample> s) => Pick(s, list => list.Max());

    private static Sample Pick(List<Sample> s, Func<List<double>, double> f) => new(
        f([.. s.Select(x => x.Ms)]), f([.. s.Select(x => x.Own)]), f([.. s.Select(x => x.Children)]), f([.. s.Select(x => x.Cpu)]),
        f([.. s.Select(x => x.Procs)]), f([.. s.Select(x => x.Alloc)]), f([.. s.Select(x => x.Handles)]), f([.. s.Select(x => x.Peak)]));
}
