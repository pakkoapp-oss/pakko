using System.Runtime.InteropServices;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Creates and configures a Job Object for a single sandboxed tar.exe invocation (absorbed from
/// T-F13's Layer 2 — see TASKS.md's T-F52 entry). The launched process must be created
/// CREATE_SUSPENDED and assigned to this job before being resumed
/// (<see cref="SandboxedProcessLauncher"/> already does this) — a fast child could otherwise
/// start running, and potentially spawn its own children, before AssignProcessToJobObject takes
/// effect.
/// </summary>
internal sealed class SandboxJobObject : IDisposable
{
    private const uint JOB_OBJECT_LIMIT_PROCESS_TIME = 0x00000002;
    private const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
    private const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    // All eight documented UI-restriction bits — tar.exe is a non-interactive console app, so
    // this closes an already-near-zero surface rather than defending anything load-bearing.
    private const uint JOB_OBJECT_UILIMIT_ALL = 0x000000FF;

    private const int JobObjectBasicUIRestrictions = 4;
    private const int JobObjectExtendedLimitInformation = 9;

    private readonly SafeJobObjectHandle _handle;

    private SandboxJobObject(SafeJobObjectHandle handle) => _handle = handle;

    public SafeJobObjectHandle Handle => _handle;

    public static SandboxJobObject Create(long ramLimitBytes, TimeSpan cpuTimeLimit)
    {
        IntPtr rawHandle = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
        if (rawHandle == IntPtr.Zero)
            throw new InvalidOperationException($"CreateJobObjectW failed (Win32 error {Marshal.GetLastWin32Error()}).");

        var handle = new SafeJobObjectHandle(rawHandle);

        try
        {
            ApplyExtendedLimits(handle, ramLimitBytes, cpuTimeLimit);
            ApplyUiRestrictions(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        return new SandboxJobObject(handle);
    }

    private static void ApplyExtendedLimits(SafeJobObjectHandle handle, long ramLimitBytes, TimeSpan cpuTimeLimit)
    {
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_ACTIVE_PROCESS
                             | JOB_OBJECT_LIMIT_PROCESS_MEMORY
                             | JOB_OBJECT_LIMIT_PROCESS_TIME
                             | JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                ActiveProcessLimit = 1,
                // TimeSpan.Ticks are already 100ns units — the same unit LARGE_INTEGER time
                // limits use, so no conversion is needed.
                PerProcessUserTimeLimit = cpuTimeLimit.Ticks,
            },
            ProcessMemoryLimit = (UIntPtr)ramLimitBytes,
        };

        int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, fDeleteOld: false);
            if (!NativeMethods.SetInformationJobObject(handle.DangerousGetHandle(), JobObjectExtendedLimitInformation, buffer, (uint)size))
                throw new InvalidOperationException($"SetInformationJobObject (extended limits) failed (Win32 error {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ApplyUiRestrictions(SafeJobObjectHandle handle)
    {
        var restrictions = new JOBOBJECT_BASIC_UI_RESTRICTIONS { UIRestrictionsClass = JOB_OBJECT_UILIMIT_ALL };

        int size = Marshal.SizeOf<JOBOBJECT_BASIC_UI_RESTRICTIONS>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(restrictions, buffer, fDeleteOld: false);
            if (!NativeMethods.SetInformationJobObject(handle.DangerousGetHandle(), JobObjectBasicUIRestrictions, buffer, (uint)size))
                throw new InvalidOperationException($"SetInformationJobObject (UI restrictions) failed (Win32 error {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_UI_RESTRICTIONS
    {
        public uint UIRestrictionsClass;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(
            IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);
    }
}
