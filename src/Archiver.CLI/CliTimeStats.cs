using System.Diagnostics;
using System.Globalization;

namespace Archiver.CLI;

/// <summary>
/// T-F408: what -bt prints after a command, laid out as 7-Zip's PrintStat (Main.cpp: PrintTime,
/// PrintMemUsage). 7-Zip's cycle-count lines need QueryProcessCycleTime, which .NET does not
/// expose; 7-Zip leaves them out too where that call is missing.
/// </summary>
public static class CliTimeStats
{
    /// <summary>The lines for this process so far.</summary>
    public static IReadOnlyList<string> ForCurrentProcess()
    {
        using Process process = Process.GetCurrentProcess();
        return Lines(
            process.PrivilegedProcessorTime, process.UserProcessorTime, DateTime.Now - process.StartTime,
            process.PeakPagedMemorySize64, process.PeakWorkingSet64);
    }

    /// <summary>A blank line, then Kernel, User, Process and Global time, as 7-Zip prints them.</summary>
    public static IReadOnlyList<string> Lines(
        TimeSpan kernel, TimeSpan user, TimeSpan global, long peakPagefileBytes, long peakWorkingSetBytes)
    {
        TimeSpan process = kernel + user;
        return
        [
            "",
            Time("Kernel ", kernel, global),
            Time("User   ", user, global),
            Time("Process", process, global) + Memory("Virtual ", peakPagefileBytes),
            Time("Global ", global, global) + Memory("Physical", peakWorkingSetBytes),
        ];
    }

    // TimeSpan ticks are 7-Zip's 100 ns FILETIME units (kFreq = 10000000).
    private static string Time(string name, TimeSpan value, TimeSpan total)
    {
        long ticks = Math.Max(0, value.Ticks);
        long seconds = ticks / TimeSpan.TicksPerSecond;
        long milliseconds = (ticks - seconds * TimeSpan.TicksPerSecond) / TimeSpan.TicksPerMillisecond;
        long percent = total.Ticks > 0 ? (long)((decimal)ticks * 100 / total.Ticks) : 0;
        return string.Create(CultureInfo.InvariantCulture, $"{name} Time ={seconds,6}.{milliseconds:D3} ={percent,5}%");
    }

    // 7-Zip's SHIFT_SIZE_VALUE(val, 20): megabytes, rounded up.
    private static string Memory(string name, long bytes)
    {
        long megabytes = (Math.Max(0, bytes) + (1L << 20) - 1) >> 20;
        return string.Create(CultureInfo.InvariantCulture, $"    {name} Memory ={megabytes,7} MB");
    }
}
