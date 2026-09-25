using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// Raw CreateProcessW + STARTUPINFOEX launcher for every tar.exe launch — managed Process.Start
/// cannot express PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES or PROC_THREAD_ATTRIBUTE_HANDLE_LIST.
/// Every process is created CREATE_SUSPENDED, optionally assigned to a Job Object, then resumed —
/// this closes the race where a fast child could otherwise start running (and potentially spawn
/// its own children) before AssignProcessToJobObject takes effect. The child inherits exactly its
/// own stdout/stderr pipe ends and nothing else (T-F244 item 5): with bInheritHandles = TRUE alone
/// it inherited every inheritable handle in this process, including another launch's pipes.
/// </summary>
internal static class SandboxedProcessLauncher
{
    private const uint STARTF_USESTDHANDLES = 0x00000100;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;

    // How long a terminated child gets to actually go away before the call returns anyway.
    private const uint TerminateWaitMilliseconds = 5000;

    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        ProcessLaunchOptions options,
        CancellationToken cancellationToken)
    {
        // T-F266: refused before anything is created — tar.exe would receive it altered.
        TarCommandLineEncoding.EnsureRepresentable([fileName, .. arguments]);

        // T-F244 item 1: each pipe end is owned by a SafeHandle from the moment it exists, so a
        // failure anywhere below releases every handle created so far.
        CreateInheritablePipe(out SafeFileHandle stdOutRead, out SafeFileHandle stdOutWrite, "stdout");
        using (stdOutRead)
        using (stdOutWrite)
        {
            CreateInheritablePipe(out SafeFileHandle stdErrRead, out SafeFileHandle stdErrWrite, "stderr");
            using (stdErrRead)
            using (stdErrWrite)
            {
                PROCESS_INFORMATION processInfo = CreateSuspendedProcess(fileName, arguments, options, stdOutWrite, stdErrWrite);

                // The child holds its own copies of the write ends now — ours must close, otherwise
                // the read ends never see EOF (classic pipe-handle-leak deadlock).
                stdOutWrite.Dispose();
                stdErrWrite.Dispose();

                using var processHandle = new SafeProcessOrThreadHandle(processInfo.hProcess);
                using var threadHandle = new SafeProcessOrThreadHandle(processInfo.hThread);
                options.OnProcessStarted?.Invoke(processInfo.dwProcessId);

                try
                {
                    return await RunCreatedProcessAsync(processHandle, threadHandle, options, stdOutRead, stdErrRead, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Any failure or a cancel: the child must be gone before this returns — a
                    // caller then deletes the folders it was writing into (T-F244 item 1).
                    TerminateAndWait(processHandle);
                    throw;
                }
            }
        }
    }

    // CreateProcessW with STARTUPINFOEX: the attribute list (AppContainer + handle list) and every
    // raw value it holds stay pinned until CreateProcessW has returned (S3869) — the list stores
    // raw handle/SID values, not SafeHandles the marshaller could pin by itself.
    private static PROCESS_INFORMATION CreateSuspendedProcess(
        string fileName, IReadOnlyList<string> arguments, ProcessLaunchOptions options,
        SafeFileHandle stdOutWrite, SafeFileHandle stdErrWrite)
    {
        // char[] (not StringBuilder) -- avoids the extra native<->managed StringBuilder marshaling
        // copy CA1838 flags; CreateProcessW's real lpCommandLine is a writable LPWSTR buffer, so
        // this still needs to be a genuinely mutable array, not a string.
        string commandLine = BuildCommandLine(fileName, arguments);
        var commandLineBuffer = new char[commandLine.Length + 1];
        commandLine.CopyTo(0, commandLineBuffer, 0, commandLine.Length);

        bool outRef = false, errRef = false, sidRef = false, inRef = false;
        try
        {
            stdOutWrite.DangerousAddRef(ref outRef);
            stdErrWrite.DangerousAddRef(ref errRef);
            options.AppContainerSid?.DangerousAddRef(ref sidRef);
            options.StdIn?.DangerousAddRef(ref inRef);
            IntPtr rawStdOut = stdOutWrite.DangerousGetHandle(); // NOSONAR: S3869 — raw value goes into STARTUPINFO and the handle list; pinned by DangerousAddRef above until CreateProcessW returns
            IntPtr rawStdErr = stdErrWrite.DangerousGetHandle(); // NOSONAR: S3869 — same
            IntPtr rawSid = options.AppContainerSid?.DangerousGetHandle() ?? IntPtr.Zero; // NOSONAR: S3869 — PSID struct field, pinned the same way
            IntPtr rawStdIn = options.StdIn?.DangerousGetHandle() ?? IntPtr.Zero; // NOSONAR: S3869 — same

            List<IntPtr> inherited = [rawStdOut, rawStdErr];
            if (rawStdIn != IntPtr.Zero)
            {
                // Only a handle named in the handle list below is ever inherited, so marking the
                // caller's handle inheritable leaks it to no other launch.
                if (!NativeMethods.SetHandleInformation(options.StdIn!, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT))
                    throw new IOException($"SetHandleInformation (stdin) failed (Win32 error {Marshal.GetLastWin32Error()}).");
                inherited.Add(rawStdIn);
            }

            using var attributes = LaunchAttributeList.Create(rawSid, inherited);

            bool attrRef = false;
            try
            {
                attributes.AttributeList.DangerousAddRef(ref attrRef);
                var startupInfoEx = new STARTUPINFOEX();
                // Must be sizeof(STARTUPINFOEX), not sizeof(STARTUPINFO) — CreateProcessW uses this
                // field to detect the extended struct is present, and a wrong size here is a
                // documented easy mistake (see TASKS.md's T-F52 design notes).
                startupInfoEx.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
                startupInfoEx.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
                startupInfoEx.StartupInfo.hStdOutput = rawStdOut;
                startupInfoEx.StartupInfo.hStdError = rawStdErr;
                startupInfoEx.StartupInfo.hStdInput = rawStdIn;
                startupInfoEx.lpAttributeList = attributes.AttributeList.DangerousGetHandle(); // NOSONAR: S3869 — struct field, pinned by DangerousAddRef above

                bool created = NativeMethods.CreateProcessW(
                    lpApplicationName: null,
                    commandLineBuffer,
                    lpProcessAttributes: IntPtr.Zero,
                    lpThreadAttributes: IntPtr.Zero,
                    bInheritHandles: true,
                    CREATE_NO_WINDOW | CREATE_SUSPENDED | EXTENDED_STARTUPINFO_PRESENT,
                    lpEnvironment: IntPtr.Zero,
                    options.WorkingDirectory,
                    ref startupInfoEx,
                    out PROCESS_INFORMATION processInfo);

                if (!created)
                    throw new IOException($"CreateProcessW failed for '{fileName}' (Win32 error {Marshal.GetLastWin32Error()}).");
                return processInfo;
            }
            finally
            {
                if (attrRef)
                    attributes.AttributeList.DangerousRelease();
            }
        }
        catch (InvalidOperationException ex)
        {
            // Attribute-list setup failure — same shape as a failed launch for every caller.
            throw new IOException(ex.Message, ex);
        }
        finally
        {
            if (inRef)
                options.StdIn!.DangerousRelease();
            if (sidRef)
                options.AppContainerSid!.DangerousRelease();
            if (errRef)
                stdErrWrite.DangerousRelease();
            if (outRef)
                stdOutWrite.DangerousRelease();
        }
    }

    // T-F138: every native call below takes processHandle/threadHandle/job directly as
    // SafeHandle-typed P/Invoke parameters — the CLR marshaller pins/releases each one itself.
    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCreatedProcessAsync(
        SafeProcessOrThreadHandle processHandle, SafeProcessOrThreadHandle threadHandle, ProcessLaunchOptions options,
        SafeFileHandle stdOutRead, SafeFileHandle stdErrRead, CancellationToken cancellationToken)
    {
        if (options.Job is not null && !NativeMethods.AssignProcessToJobObject(options.Job, processHandle))
            throw new IOException($"AssignProcessToJobObject failed (Win32 error {Marshal.GetLastWin32Error()}).");

        if (NativeMethods.ResumeThread(threadHandle) == uint.MaxValue)
            throw new IOException($"ResumeThread failed (Win32 error {Marshal.GetLastWin32Error()}).");

        using var stdOutStream = new FileStream(stdOutRead, FileAccess.Read);
        using var stdErrStream = new FileStream(stdErrRead, FileAccess.Read);
        using var stdOutReader = new StreamReader(stdOutStream);
        using var stdErrReader = new StreamReader(stdErrStream);

        Task<string> stdOutTask = stdOutReader.ReadToEndAsync(cancellationToken);
        Task<string> stdErrTask = options.OnStdErrLine is { } onLine
            ? ReadLinesAsync(stdErrReader, onLine, cancellationToken)
            : stdErrReader.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdOutTask, stdErrTask).ConfigureAwait(false);

        await WaitForExitAsync(processHandle, cancellationToken).ConfigureAwait(false);

        if (!NativeMethods.GetExitCodeProcess(processHandle, out uint exitCode))
            throw new IOException($"GetExitCodeProcess failed (Win32 error {Marshal.GetLastWin32Error()}).");

        return ((int)exitCode, stdOutTask.Result, stdErrTask.Result);
    }

    // tar.exe's "-v" writes one "a <name>" line per entry to stderr during creation — streamed so
    // the caller can report progress while the process runs.
    private static async Task<string> ReadLinesAsync(StreamReader reader, Action<string> onLine, CancellationToken cancellationToken)
    {
        var all = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            all.AppendLine(line);
            if (line.Length > 0)
                onLine(line);
        }
        return all.ToString();
    }

    private static void TerminateAndWait(SafeProcessOrThreadHandle processHandle)
    {
        try
        {
            // Terminate fails harmlessly if the child already exited; the wait covers both cases.
            _ = NativeMethods.TerminateProcess(processHandle, 1);
            _ = NativeMethods.WaitForSingleObject(processHandle, TerminateWaitMilliseconds);
        }
        catch { /* best-effort — the original exception is what the caller needs */ }
    }

    // Pure pipe-pair setup. The read end stays ours only (not inheritable); the write end is
    // inheritable, but only a launch whose handle list names it can actually receive it.
    private static void CreateInheritablePipe(out SafeFileHandle readEnd, out SafeFileHandle writeEnd, string pipeName)
    {
        var pipeSecurity = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            lpSecurityDescriptor = IntPtr.Zero,
            bInheritHandle = true,
        };
        if (!NativeMethods.CreatePipe(out readEnd, out writeEnd, ref pipeSecurity, 0))
            throw new IOException($"CreatePipe ({pipeName}) failed (Win32 error {Marshal.GetLastWin32Error()}).");
        if (!NativeMethods.SetHandleInformation(readEnd, HANDLE_FLAG_INHERIT, 0))
        {
            int error = Marshal.GetLastWin32Error();
            readEnd.Dispose();
            writeEnd.Dispose();
            throw new IOException($"SetHandleInformation ({pipeName} read end) failed (Win32 error {error}).");
        }
    }

    // Waits for the process handle to become signaled (process exit) without blocking a
    // dedicated thread for the duration — ThreadPool.RegisterWaitForSingleObject schedules the
    // continuation only once the kernel object actually signals.
    private static Task WaitForExitAsync(SafeProcessOrThreadHandle processHandle, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The raw handle backs a second, non-owning SafeWaitHandle the OS wait dereferences for
        // the whole wait — so the reference taken here is held until the wait is unregistered,
        // not just for the read (T-F244 item 1, S3869). ManualResetEvent.SafeWaitHandle needs an
        // already-constructed SafeWaitHandle, not a P/Invoke parameter the marshaller could pin.
        bool refAdded = false;
        processHandle.DangerousAddRef(ref refAdded);
        IntPtr rawHandle = processHandle.DangerousGetHandle(); // NOSONAR: S3869 — pinned by DangerousAddRef until the continuation below releases it

        var waitHandle = new ManualResetEvent(false)
        {
            SafeWaitHandle = new SafeWaitHandle(rawHandle, ownsHandle: false),
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
            // On a cancel the wait is still pending — only once the pool confirms it let go of the
            // raw handle may the reference below be released.
            using (var unregistered = new ManualResetEvent(false))
            {
                if (registeredWait.Unregister(unregistered))
                    unregistered.WaitOne();
            }
            ctr.Dispose();
            waitHandle.Dispose();
            if (refAdded)
                processHandle.DangerousRelease();
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
                AppendBackslashRun(sb, argument, ref i);
            else if (c == '"')
                sb.Append('\\').Append('"');
            else
                sb.Append(c);
        }
        sb.Append('"');
    }

    // A run of backslashes must be doubled when followed by a literal quote (so the quote itself
    // still escapes correctly), or left as-is otherwise -- one already-consumed backslash plus
    // however many more immediately follow it. `i` is the index right after the already-consumed
    // backslash; advanced in place to just past the run this call accounts for.
    private static void AppendBackslashRun(StringBuilder sb, string argument, ref int i)
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

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
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
    private struct STARTUPINFOEX // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION // NOSONAR: S101 — mirrors the real Win32 SDK struct name (see docs/CONVENTIONS.md)
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
            out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetHandleInformation(SafeFileHandle hObject, uint dwMask, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(SafeProcessOrThreadHandle hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessW(
            string? lpApplicationName,
            char[] lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref STARTUPINFOEX lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint ResumeThread(SafeProcessOrThreadHandle hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(SafeProcessOrThreadHandle hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeProcess(SafeProcessOrThreadHandle hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(SafeJobObjectHandle hJob, SafeProcessOrThreadHandle hProcess);
    }
}
