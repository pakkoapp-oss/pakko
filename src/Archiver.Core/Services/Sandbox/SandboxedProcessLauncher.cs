using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Raw CreateProcessW + STARTUPINFOEX launcher — managed Process.Start cannot express
/// PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES, so every sandboxed tar.exe invocation goes
/// through here instead of TarProcessService's original Process.Start-based RunTarAsync.
/// Every process is created CREATE_SUSPENDED, optionally assigned to a Job Object, then resumed —
/// this closes the race where a fast child could otherwise start running (and potentially spawn
/// its own children) before AssignProcessToJobObject takes effect.
/// </summary>
internal static class SandboxedProcessLauncher
{
    private const uint STARTF_USESTDHANDLES = 0x00000100;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;

    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        SafeProcThreadAttributeListHandle? attributeList,
        SafeJobObjectHandle? jobObject,
        CancellationToken cancellationToken)
    {
        var pipeSecurity = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            lpSecurityDescriptor = IntPtr.Zero,
            bInheritHandle = true,
        };

        if (!NativeMethods.CreatePipe(out IntPtr stdOutRead, out IntPtr stdOutWrite, ref pipeSecurity, 0))
            throw new IOException("CreatePipe (stdout) failed.");
        if (!NativeMethods.SetHandleInformation(stdOutRead, HANDLE_FLAG_INHERIT, 0))
            throw new IOException("SetHandleInformation (stdout read end) failed.");

        if (!NativeMethods.CreatePipe(out IntPtr stdErrRead, out IntPtr stdErrWrite, ref pipeSecurity, 0))
            throw new IOException("CreatePipe (stderr) failed.");
        if (!NativeMethods.SetHandleInformation(stdErrRead, HANDLE_FLAG_INHERIT, 0))
            throw new IOException("SetHandleInformation (stderr read end) failed.");

        var startupInfoEx = new STARTUPINFOEX();
        // Must be sizeof(STARTUPINFOEX), not sizeof(STARTUPINFO) — CreateProcessW uses this field
        // to detect the extended struct is present, and a wrong size here is a documented easy
        // mistake (see TASKS.md's T-F52 design notes).
        startupInfoEx.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        startupInfoEx.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
        startupInfoEx.StartupInfo.hStdOutput = stdOutWrite;
        startupInfoEx.StartupInfo.hStdError = stdErrWrite;
        startupInfoEx.StartupInfo.hStdInput = IntPtr.Zero;
        startupInfoEx.lpAttributeList = attributeList?.DangerousGetHandle() ?? IntPtr.Zero;

        uint creationFlags = CREATE_NO_WINDOW | CREATE_SUSPENDED;
        if (attributeList is not null)
            creationFlags |= EXTENDED_STARTUPINFO_PRESENT;

        var commandLineBuffer = new StringBuilder(BuildCommandLine(fileName, arguments));

        bool created = NativeMethods.CreateProcessW(
            lpApplicationName: null,
            commandLineBuffer,
            lpProcessAttributes: IntPtr.Zero,
            lpThreadAttributes: IntPtr.Zero,
            bInheritHandles: true,
            creationFlags,
            lpEnvironment: IntPtr.Zero,
            lpCurrentDirectory: null,
            ref startupInfoEx,
            out PROCESS_INFORMATION processInfo);

        // The child inherited its own copies of the write ends — our copies must close regardless
        // of success, otherwise the read ends never see EOF (classic pipe-handle-leak deadlock).
        NativeMethods.CloseHandle(stdOutWrite);
        NativeMethods.CloseHandle(stdErrWrite);

        if (!created)
        {
            int error = Marshal.GetLastWin32Error();
            NativeMethods.CloseHandle(stdOutRead);
            NativeMethods.CloseHandle(stdErrRead);
            throw new IOException($"CreateProcessW failed for '{fileName}' (Win32 error {error}).");
        }

        using var processHandle = new SafeProcessOrThreadHandle(processInfo.hProcess);
        using var threadHandle = new SafeProcessOrThreadHandle(processInfo.hThread);

        try
        {
            if (jobObject is not null &&
                !NativeMethods.AssignProcessToJobObject(jobObject.DangerousGetHandle(), processHandle.DangerousGetHandle()))
            {
                int error = Marshal.GetLastWin32Error();
                try { NativeMethods.TerminateProcess(processHandle.DangerousGetHandle(), 1); } catch { }
                throw new IOException($"AssignProcessToJobObject failed (Win32 error {error}).");
            }

            if (NativeMethods.ResumeThread(threadHandle.DangerousGetHandle()) == uint.MaxValue)
            {
                int error = Marshal.GetLastWin32Error();
                try { NativeMethods.TerminateProcess(processHandle.DangerousGetHandle(), 1); } catch { }
                throw new IOException($"ResumeThread failed (Win32 error {error}).");
            }

            using var stdOutStream = new FileStream(new SafeFileHandle(stdOutRead, ownsHandle: true), FileAccess.Read);
            using var stdErrStream = new FileStream(new SafeFileHandle(stdErrRead, ownsHandle: true), FileAccess.Read);
            using var stdOutReader = new StreamReader(stdOutStream);
            using var stdErrReader = new StreamReader(stdErrStream);

            Task<string> stdOutTask = stdOutReader.ReadToEndAsync(cancellationToken);
            Task<string> stdErrTask = stdErrReader.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(stdOutTask, stdErrTask).ConfigureAwait(false);

            await WaitForExitAsync(processHandle, cancellationToken).ConfigureAwait(false);

            if (!NativeMethods.GetExitCodeProcess(processHandle.DangerousGetHandle(), out uint exitCode))
                throw new IOException($"GetExitCodeProcess failed (Win32 error {Marshal.GetLastWin32Error()}).");

            return ((int)exitCode, stdOutTask.Result, stdErrTask.Result);
        }
        catch (OperationCanceledException)
        {
            try { NativeMethods.TerminateProcess(processHandle.DangerousGetHandle(), 1); } catch { }
            throw;
        }
    }

    // Waits for the process handle to become signaled (process exit) without blocking a
    // dedicated thread for the duration — ThreadPool.RegisterWaitForSingleObject schedules the
    // continuation only once the kernel object actually signals.
    private static Task WaitForExitAsync(SafeProcessOrThreadHandle processHandle, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitHandle = new ManualResetEvent(false)
        {
            SafeWaitHandle = new SafeWaitHandle(processHandle.DangerousGetHandle(), ownsHandle: false),
        };

        RegisteredWaitHandle registeredWait = ThreadPool.RegisterWaitForSingleObject(
            waitHandle,
            (_, _) => tcs.TrySetResult(),
            state: null,
            timeout: Timeout.InfiniteTimeSpan,
            executeOnlyOnce: true);

        CancellationTokenRegistration ctr = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        return tcs.Task.ContinueWith(t =>
        {
            registeredWait.Unregister(null);
            ctr.Dispose();
            waitHandle.Dispose();
            return t;
        }, TaskScheduler.Default).Unwrap();
    }

    // Standard Win32 command-line quoting (the same algorithm the Windows C runtime's argv parser
    // expects): only quote an argument when needed, and escape a run of backslashes based on
    // whether it's immediately followed by a literal quote.
    private static string BuildCommandLine(string fileName, IReadOnlyList<string> arguments)
    {
        var sb = new StringBuilder();
        AppendArgument(sb, fileName);
        foreach (string argument in arguments)
        {
            sb.Append(' ');
            AppendArgument(sb, argument);
        }
        return sb.ToString();
    }

    private static void AppendArgument(StringBuilder sb, string argument)
    {
        if (argument.Length != 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            sb.Append(argument);
            return;
        }

        sb.Append('"');
        for (int i = 0; i < argument.Length;)
        {
            char c = argument[i++];
            if (c == '\\')
            {
                int backslashCount = 1;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashCount++;
                    i++;
                }

                if (i == argument.Length)
                    sb.Append('\\', backslashCount * 2);
                else if (argument[i] == '"')
                {
                    sb.Append('\\', backslashCount * 2 + 1);
                    sb.Append('"');
                    i++;
                }
                else
                    sb.Append('\\', backslashCount);
            }
            else if (c == '"')
                sb.Append('\\').Append('"');
            else
                sb.Append(c);
        }
        sb.Append('"');
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreatePipe(
            out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessW(
            string? lpApplicationName,
            StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref STARTUPINFOEX lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
