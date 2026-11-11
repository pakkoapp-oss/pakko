using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Litmus;

/// <summary>A Job object handle, closed with the handle.</summary>
internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeJobHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle() => Native.CloseHandle(handle);
}

[StructLayout(LayoutKind.Sequential)]
internal struct JobAccounting
{
    public long TotalUserTime;
    public long TotalKernelTime;
    public long ThisPeriodTotalUserTime;
    public long ThisPeriodTotalKernelTime;
    public uint TotalPageFaultCount;
    public uint TotalProcesses;
    public uint ActiveProcesses;
    public uint TotalTerminatedProcesses;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessMemoryCounters
{
    public uint Cb;
    public uint PageFaultCount;
    public nuint PeakWorkingSetSize;
    public nuint WorkingSetSize;
    public nuint QuotaPeakPagedPoolUsage;
    public nuint QuotaPagedPoolUsage;
    public nuint QuotaPeakNonPagedPoolUsage;
    public nuint QuotaNonPagedPoolUsage;
    public nuint PagefileUsage;
    public nuint PeakPagefileUsage;
}

internal static partial class Native
{
    private const int JobObjectBasicAccountingInformation = 1;

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeJobHandle CreateJobObjectW(nint attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(nint job, int infoClass, out JobAccounting info, int length, out int returned);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMemoryInfo(nint process, out ProcessMemoryCounters counters, int size);

    /// <summary>The accounting of the job this process runs in (a null handle means the caller's own).</summary>
    public static JobAccounting OwnJob()
    {
        if (!QueryInformationJobObject(0, JobObjectBasicAccountingInformation, out JobAccounting info, Marshal.SizeOf<JobAccounting>(), out _))
            throw new InvalidOperationException($"QueryInformationJobObject failed (Win32 error {Marshal.GetLastPInvokeError()}); run under the litmus parent.");
        return info;
    }

    public static ProcessMemoryCounters OwnMemory()
    {
        var counters = new ProcessMemoryCounters { Cb = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
        if (!GetProcessMemoryInfo(GetCurrentProcess(), out counters, (int)counters.Cb))
            throw new InvalidOperationException($"GetProcessMemoryInfo failed (Win32 error {Marshal.GetLastPInvokeError()}).");
        return counters;
    }
}
