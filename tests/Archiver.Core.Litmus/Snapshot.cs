using System.Diagnostics;
using System.Globalization;

namespace Archiver.Core.Litmus;

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
