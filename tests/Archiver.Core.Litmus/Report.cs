using System.Globalization;
using System.Text;

namespace Archiver.Core.Litmus;

/// <summary>
/// The table. Per entry and per MiB of input, and the excess over the floor the file system costs
/// for the same input (floor-read + floor-write, same shape, same run). The ratio to the floor is
/// shown only where the floor is not trivial (T-F498: a read from the file cache can cost almost no
/// faults of the process).
/// </summary>
internal static class Report
{
    private const double MiB = 1024 * 1024;
    private const double TrivialFloorFaults = 100;

    public static void Print(
        List<(Operation Op, string Shape, Sample? Median, Sample? Min, Sample? Max, string? Skip)> rows,
        Dictionary<string, Shape> shapes, int scale, int runs)
    {
        Console.WriteLine($"## T-F498 litmus run: scale x{scale}, median of {runs} (min-max of faults in brackets)");
        Console.WriteLine();
        foreach (string shapeName in rows.Select(r => r.Shape).Distinct())
        {
            Shape shape = shapes[shapeName];
            double floor = FloorFaults(rows, shapeName);
            Console.WriteLine($"### {shapeName}: {shape.Files} file(s), {shape.Bytes / MiB:F1} MiB; floor (read + write) {floor:F0} faults");
            Console.WriteLine();
            Console.WriteLine("| operation | ms | faults (own) | faults (children) | per entry | per MiB | over floor per entry | x floor | alloc MiB | handles | processes |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach ((Operation op, string _, Sample? m, Sample? min, Sample? max, string? skip) in rows.Where(r => r.Shape == shapeName))
            {
                if (m is null)
                {
                    Console.WriteLine($"| {op.Name} | skipped: {skip} |||||||||");
                    continue;
                }
                double faults = m.Own + m.Children;
                string ratio = floor >= TrivialFloorFaults ? (faults / floor).ToString("F1", CultureInfo.InvariantCulture) : "-";
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {op.Name} | {m.Ms:F0} | {m.Own:F0} [{min!.Own:F0}-{max!.Own:F0}] | {m.Children:F0} | {faults / shape.Files:F2} | {faults / Math.Max(shape.Bytes / MiB, 1e-9):F1} | {(faults - floor) / shape.Files:F2} | {ratio} | {m.Alloc / MiB:F1} | {m.Handles:F0} | {m.Procs:F0} |"));
            }
            Console.WriteLine();
        }
    }

    public static void WriteCsv(string path,
        List<(Operation Op, string Shape, Sample? Median, Sample? Min, Sample? Max, string? Skip)> rows,
        Dictionary<string, Shape> shapes)
    {
        var text = new StringBuilder("operation,shape,files,bytes,ms,own,children,cpu_ms,processes,alloc,handles,peak_ws,own_min,own_max,skip\n");
        foreach ((Operation op, string shapeName, Sample? m, Sample? min, Sample? max, string? skip) in rows)
        {
            Shape shape = shapes[shapeName];
            text.Append(CultureInfo.InvariantCulture,
                $"{op.Name},{shapeName},{shape.Files},{shape.Bytes},{m?.Ms},{m?.Own},{m?.Children},{m?.Cpu},{m?.Procs},{m?.Alloc},{m?.Handles},{m?.Peak},{min?.Own},{max?.Own},\"{skip}\"\n");
        }
        File.WriteAllText(path, text.ToString());
    }

    private static double FloorFaults(
        List<(Operation Op, string Shape, Sample? Median, Sample? Min, Sample? Max, string? Skip)> rows, string shape) =>
        rows.Where(r => r.Shape == shape && r.Op.Name is "floor-read" or "floor-write" && r.Median is not null)
            .Sum(r => r.Median!.Own + r.Median.Children);
}
