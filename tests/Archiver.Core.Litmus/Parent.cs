using System.Diagnostics;
using System.Globalization;
using Archiver.Core.Models;
using Archiver.Core.Services;

namespace Archiver.Core.Litmus;

internal static class Parent
{
    private const string Usage = "Usage: [--runs N] [--scale N] [--ops a,b] [--shapes many,one] [--csv file]";

    private sealed record Options(int Runs, int Scale, string[]? Only, string[] Shapes, string? Csv);

    public static async Task<int> RunAsync(string[] args)
    {
        Options? options = ParseOptions(args, out string? error);
        if (options is null)
        {
            await Console.Error.WriteLineAsync(error);
            return 2;
        }
        (int runs, int scale, string[]? only, string[] shapes, string? csv) = options;

        await Console.Error.WriteLineAsync("Preparing fixtures (once per scale)...");
        Dictionary<string, Shape> fixtures = await Fixtures.PrepareAsync(Fixtures.RootFor(scale), scale, PakkoServices.Create(new GroupPolicyOptions()));

        var rows = new List<(Operation Op, string Shape, Sample? Median, Sample? Min, Sample? Max, string? Skip)>();
        foreach (Operation op in Operations.All.Where(o => only is null || only.Contains(o.Name)))
        {
            foreach (string shape in shapes)
                rows.Add(await MeasureAsync(op, shape, runs, scale));
        }

        Report.Print(rows, fixtures, scale, runs);
        if (csv is not null)
            Report.WriteCsv(csv, rows, fixtures);
        return 0;
    }

    private static Options? ParseOptions(string[] args, out string? error)
    {
        var options = new Options(3, 1, null, Fixtures.Measured, null);
        var queue = new Queue<string>(args);
        while (queue.TryDequeue(out string? name))
        {
            if (!queue.TryDequeue(out string? value))
            {
                error = $"{name} needs a value. {Usage}";
                return null;
            }
            switch (name)
            {
                case "--runs": options = options with { Runs = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                case "--scale": options = options with { Scale = int.Parse(value, CultureInfo.InvariantCulture) }; break;
                case "--ops": options = options with { Only = value.Split(',') }; break;
                case "--shapes": options = options with { Shapes = value.Split(',') }; break;
                case "--csv": options = options with { Csv = value }; break;
                default:
                    error = $"Unknown argument {name}. {Usage}";
                    return null;
            }
        }
        if (options.Only?.FirstOrDefault(name => Operations.All.All(o => o.Name != name)) is { } unknown)
        {
            error = $"Unknown operation {unknown}: {string.Join(", ", Operations.All.Select(o => o.Name))}";
            return null;
        }
        error = null;
        return options;
    }

    private static async Task<(Operation Op, string Shape, Sample? Median, Sample? Min, Sample? Max, string? Skip)> MeasureAsync(
        Operation op, string shape, int runs, int scale)
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
        await Console.Error.WriteLineAsync($"{op.Name} {shape}: {skip ?? $"{samples.Count} runs"}");
        return skip is not null || samples.Count == 0
            ? (op, shape, null, null, null, skip ?? "no result")
            : (op, shape, Median(samples), MinOf(samples), MaxOf(samples), null);
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
