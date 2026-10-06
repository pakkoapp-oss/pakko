using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Archiver.Shell;

/// <summary>
/// T-F356: a child process that inherits only the handles named for it
/// (PROC_THREAD_ATTRIBUTE_HANDLE_LIST). <c>Process.Start</c> lets a child inherit every
/// inheritable handle open at that moment; a helper started while tar.exe is being launched would
/// then hold tar's pipe ends, and the read of tar's output would not end until the helper did.
/// </summary>
internal sealed partial class HandleListProcess : IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;

    // ProcThreadAttributeValue(ProcThreadAttributeHandleList = 2, Thread = FALSE, Input = TRUE,
    // Additive = FALSE) = 0x00020002.
    private static readonly IntPtr ProcThreadAttributeHandleList = 0x00020002;

    private readonly SafeProcessHandle _process;

    private HandleListProcess(SafeProcessHandle process, int id)
    {
        _process = process;
        Id = id;
    }

    public int Id { get; }

    /// <summary>
    /// Starts <paramref name="exePath"/> (an absolute path). Every handle in
    /// <paramref name="inherited"/> must already be inheritable.
    /// </summary>
    public static HandleListProcess Start(string exePath, IReadOnlyList<string> arguments, IReadOnlyList<SafeHandle> inherited)
    {
        // CreateProcessW may write into the command line, so it gets its own buffer.
        char[] commandLine = [.. BuildCommandLine(exePath, arguments), '\0'];

        var pinned = new List<SafeHandle>(inherited.Count);
        try
        {
            IntPtr[] raw = new IntPtr[inherited.Count];
            for (int i = 0; i < inherited.Count; i++)
            {
                bool added = false;
                inherited[i].DangerousAddRef(ref added);
                if (added)
                    pinned.Add(inherited[i]);
                raw[i] = inherited[i].DangerousGetHandle(); // NOSONAR: S3869 — goes into the handle list; pinned by DangerousAddRef until CreateProcessW returns
            }

            using var attributes = new HandleListAttribute(raw);
            var startup = new STARTUPINFOEX { lpAttributeList = attributes.List };
            startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

            if (!CreateProcessW(exePath, commandLine, IntPtr.Zero, IntPtr.Zero, bInheritHandles: true,
                    ExtendedStartupInfoPresent, IntPtr.Zero, null, ref startup, out PROCESS_INFORMATION info))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            _ = CloseHandle(info.hThread);
            return new HandleListProcess(new SafeProcessHandle(info.hProcess, ownsHandle: true), info.dwProcessId);
        }
        finally
        {
            foreach (SafeHandle handle in pinned)
                handle.DangerousRelease();
        }
    }

    /// <summary>Best effort and idempotent: ends the process if it still runs.</summary>
    public void Kill()
    {
        try
        {
            _ = TerminateProcess(_process, 1);
        }
        catch (ObjectDisposedException)
        {
            // Already disposed; nothing left to end.
        }
    }

    public void Dispose() => _process.Dispose();

    // The rules CommandLineToArgvW reads back: quotes around everything, a quote escaped with a
    // backslash, and the backslashes before a quote doubled.
    internal static string BuildCommandLine(string exePath, IReadOnlyList<string> arguments)
    {
        var line = new StringBuilder();
        AppendQuoted(line, exePath);
        foreach (string argument in arguments)
        {
            line.Append(' ');
            AppendQuoted(line, argument);
        }
        return line.ToString();
    }

    private static void AppendQuoted(StringBuilder line, string value)
    {
        line.Append('"');
        int backslashes = 0;
        foreach (char c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
                line.Append('\\', backslashes * 2 + 1);
            else
                line.Append('\\', backslashes);
            backslashes = 0;
            line.Append(c);
        }
        line.Append('\\', backslashes * 2).Append('"');
    }

    // The attribute list points at the handle array without copying it: both live until Dispose.
    private sealed class HandleListAttribute : IDisposable
    {
        private IntPtr _handles;
        private bool _initialized;

        public HandleListAttribute(IntPtr[] handles)
        {
            try
            {
                IntPtr size = IntPtr.Zero;
                // Expected to fail: this call only reports the size.
                _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                List = Marshal.AllocHGlobal(size);
                if (!InitializeProcThreadAttributeList(List, 1, 0, ref size))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                _initialized = true;

                int bytes = IntPtr.Size * handles.Length;
                _handles = Marshal.AllocHGlobal(bytes);
                Marshal.Copy(handles, 0, _handles, handles.Length);
                if (!UpdateProcThreadAttribute(List, 0, ProcThreadAttributeHandleList, _handles, bytes, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public IntPtr List { get; private set; }

        public void Dispose()
        {
            if (_initialized)
                DeleteProcThreadAttributeList(List);
            _initialized = false;
            Marshal.FreeHGlobal(List);
            Marshal.FreeHGlobal(_handles);
            List = IntPtr.Zero;
            _handles = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO // NOSONAR: S101 — mirrors the Win32 SDK struct name (docs/CONVENTIONS.md)
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
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX // NOSONAR: S101 — mirrors the Win32 SDK struct name (docs/CONVENTIONS.md)
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION // NOSONAR: S101 — mirrors the Win32 SDK struct name (docs/CONVENTIONS.md)
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(
        string lpApplicationName, [In, Out] char[] lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
        string? lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeProcessHandle hProcess, uint uExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);
}
