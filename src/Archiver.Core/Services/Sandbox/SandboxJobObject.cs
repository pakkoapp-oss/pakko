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
    private const int JobObjectAssociateCompletionPortInformation = 7;
    private const int JobObjectExtendedLimitInformation = 9;

    // Job notifications (winnt.h JOB_OBJECT_MSG_*).
    private const uint JobMsgEndOfProcessTime = 2;
    private const uint JobMsgExitProcess = 7;
    private const uint JobMsgAbnormalExitProcess = 8;
    private const uint JobMsgProcessMemoryLimit = 9;

    // How long ReadLimitHit waits for the job to report the process's exit.
    private const uint ExitMessageWaitMilliseconds = 1000;

    private readonly SafeJobObjectHandle _handle;
    private readonly SafeCompletionPortHandle _port;

    private SandboxJobObject(SafeJobObjectHandle handle, SafeCompletionPortHandle port)
    {
        _handle = handle;
        _port = port;
    }

    public SafeJobObjectHandle Handle => _handle;

    /// <summary>Which Job limit stopped the process, if any (T-F239).</summary>
    public enum LimitHit
    {
        None,
        Memory,
        CpuTime,
    }

    public static SandboxJobObject Create(long ramLimitBytes, TimeSpan cpuTimeLimit)
    {
        IntPtr rawHandle = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
        if (rawHandle == IntPtr.Zero)
            throw new InvalidOperationException($"CreateJobObjectW failed (Win32 error {Marshal.GetLastWin32Error()}).");

        var handle = new SafeJobObjectHandle(rawHandle);
        SafeCompletionPortHandle? port = null;

        try
        {
            ApplyExtendedLimits(handle, ramLimitBytes, cpuTimeLimit);
            ApplyUiRestrictions(handle);
            port = AssociateCompletionPort(handle);
        }
        catch
        {
            port?.Dispose();
            handle.Dispose();
            throw;
        }

        return new SandboxJobObject(handle, port);
    }

    // T-F239: the job reports a limit it enforced on this port. A process over its memory limit
    // is not killed — its allocation fails and tar.exe prints "Cannot allocate memory", which
    // reads as the machine being out of memory; one over its CPU time is terminated silently.
    private static SafeCompletionPortHandle AssociateCompletionPort(SafeJobObjectHandle handle)
    {
        SafeCompletionPortHandle port = NativeMethods.CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, UIntPtr.Zero, 1);
        if (port.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            port.Dispose();
            throw new InvalidOperationException($"CreateIoCompletionPort failed (Win32 error {error}).");
        }

        bool portRef = false;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            port.DangerousAddRef(ref portRef);
            var association = new JOBOBJECT_ASSOCIATE_COMPLETION_PORT
            {
                CompletionKey = IntPtr.Zero,
                CompletionPort = port.DangerousGetHandle(), // NOSONAR: S3869 — struct field, pinned by DangerousAddRef until SetInformationJobObject returns
            };
            int size = Marshal.SizeOf<JOBOBJECT_ASSOCIATE_COMPLETION_PORT>();
            buffer = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(association, buffer, fDeleteOld: false);
            if (!NativeMethods.SetInformationJobObject(handle, JobObjectAssociateCompletionPortInformation, buffer, (uint)size))
                throw new InvalidOperationException($"SetInformationJobObject (completion port) failed (Win32 error {Marshal.GetLastWin32Error()}).");
            return port;
        }
        catch
        {
            port.Dispose();
            throw;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
            if (portRef)
                port.DangerousRelease();
        }
    }

    /// <summary>
    /// After the job's process has exited: which limit (if any) the job enforced on it. Reads
    /// the job's messages up to the process's exit message, waiting at most a second for it.
    /// </summary>
    public LimitHit ReadLimitHit()
    {
        LimitHit hit = LimitHit.None;
        while (NativeMethods.GetQueuedCompletionStatus(_port, out uint message, out _, out _, ExitMessageWaitMilliseconds))
        {
            if (message == JobMsgProcessMemoryLimit)
                hit = LimitHit.Memory;
            else if (message == JobMsgEndOfProcessTime)
                hit = LimitHit.CpuTime;
            else if (message is JobMsgExitProcess or JobMsgAbnormalExitProcess)
                break;
        }
        return hit;
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
            if (!NativeMethods.SetInformationJobObject(handle, JobObjectExtendedLimitInformation, buffer, (uint)size))
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
            if (!NativeMethods.SetInformationJobObject(handle, JobObjectBasicUIRestrictions, buffer, (uint)size))
                throw new InvalidOperationException($"SetInformationJobObject (UI restrictions) failed (Win32 error {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
        _port.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_ASSOCIATE_COMPLETION_PORT // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
    {
        public IntPtr CompletionKey;
        public IntPtr CompletionPort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
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
    private struct IO_COUNTERS // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_UI_RESTRICTIONS // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
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
            SafeJobObjectHandle hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeCompletionPortHandle CreateIoCompletionPort(
            IntPtr fileHandle, IntPtr existingCompletionPort, UIntPtr completionKey, uint numberOfConcurrentThreads);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetQueuedCompletionStatus(
            SafeCompletionPortHandle completionPort, out uint numberOfBytes, out UIntPtr completionKey,
            out IntPtr overlapped, uint milliseconds);
    }
}
