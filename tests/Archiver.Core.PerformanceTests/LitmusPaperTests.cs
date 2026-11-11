using System.Diagnostics;
using System.Globalization;
using FluentAssertions;

namespace Archiver.Core.PerformanceTests;

/// <summary>
/// T-F508: the page-fault litmus paper that stays. It runs tests/Archiver.Core.Litmus (built with the
/// solution) for ZIP creation over 2,000 small files and checks each row's page faults per entry
/// against the floor the file system costs for the same files. Today the rows sit at 3-4 times the
/// floor; T-F318's storm was 68 times. The 15 times limit leaves room for a runner's noise. Slow,
/// so it runs in the canary (masked), and gates nothing until five nights show it stable there.
/// </summary>
public sealed class LitmusPaperTests
{
    private const double MaxFaultsOverFloor = 15;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);

    [Fact]
    [Trait("Category", "Slow")]
    public async Task ZipCreation_ManySmallFiles_StaysNearTheFileSystemsFloor()
    {
        string csv = Path.Combine(Path.GetTempPath(), "pakko-litmus-paper-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            (int exitCode, string output) = await RunLitmusAsync(
                ["--runs", "3", "--shapes", "many", "--ops", "floor-read,floor-write,zip-optimal,zip-fastest", "--csv", csv]);
            exitCode.Should().Be(0, output);

            Dictionary<string, double> faults = ReadFaults(csv);
            double floor = faults["floor-read"] + faults["floor-write"];
            floor.Should().BePositive(output);
            foreach (string op in new[] { "zip-optimal", "zip-fastest" })
                (faults[op] / floor).Should().BeLessThan(MaxFaultsOverFloor, $"{op}: {faults[op]} faults against a floor of {floor}{Environment.NewLine}{output}");
        }
        finally
        {
            if (File.Exists(csv))
                File.Delete(csv);
        }
    }

    // Own plus children's page faults per operation (the median the tool wrote).
    private static Dictionary<string, double> ReadFaults(string csv)
    {
        string[] lines = File.ReadAllLines(csv);
        string[] header = lines[0].Split(',');
        int op = Array.IndexOf(header, "operation"), own = Array.IndexOf(header, "own"), children = Array.IndexOf(header, "children");
        var faults = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split(',');
            faults[cells[op]] = double.Parse(cells[own], CultureInfo.InvariantCulture) + double.Parse(cells[children], CultureInfo.InvariantCulture);
        }
        return faults;
    }

    private static async Task<(int ExitCode, string Output)> RunLitmusAsync(string[] arguments)
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "Archiver.Core.Litmus.exe");
        File.Exists(exe).Should().BeTrue($"the litmus tool is copied beside the tests ({exe})");
        var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        using var cts = new CancellationTokenSource(Timeout);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"the litmus run took over {Timeout.TotalMinutes} minutes");
        }
        return (process.ExitCode, await stdout + await stderr);
    }
}
