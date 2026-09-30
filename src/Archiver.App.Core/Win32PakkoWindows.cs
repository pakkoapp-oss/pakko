using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Archiver.App.Core;

/// <summary>
/// T-F201: top-left corners of the visible top-level windows of other Pakko App processes, for
/// <see cref="WindowCascade"/>.
/// </summary>
public static partial class Win32PakkoWindows
{
    /// <summary>Corners of every visible top-level window owned by another process with this process's name.</summary>
    public static IReadOnlyList<(int Left, int Top)> OtherWindowCorners()
    {
        using var current = Process.GetCurrentProcess();
        var otherPids = new HashSet<uint>();
        foreach (Process process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (process.Id != current.Id)
                    otherPids.Add((uint)process.Id);
            }
        }
        if (otherPids.Count == 0)
            return [];

        var corners = new List<(int, int)>();
        EnumWindowsProc callback = (hWnd, _) =>
        {
            if (IsWindowVisible(hWnd)
                && GetWindowThreadProcessId(hWnd, out uint pid) != 0
                && otherPids.Contains(pid)
                && GetWindow(hWnd, GwOwner) == IntPtr.Zero
                && GetWindowRect(hWnd, out Rect rect))
                corners.Add((rect.Left, rect.Top));
            return true;
        };
        // [LibraryImport] does not marshal delegates: pass the thunk and keep the delegate alive
        // until EnumWindows (synchronous) has returned.
        EnumWindows(Marshal.GetFunctionPointerForDelegate(callback), IntPtr.Zero);
        GC.KeepAlive(callback);
        return corners;
    }

    private const uint GwOwner = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(IntPtr lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out Rect lpRect);
}
