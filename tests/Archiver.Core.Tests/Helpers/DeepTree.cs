using System.Runtime.InteropServices;

namespace Archiver.Core.Tests.Helpers;

/// <summary>
/// T-F237: a folder chain <c>root/d/d/.../d</c> of a given depth. Every Windows path call walks the
/// whole path, so building and deleting such a chain costs O(depth^2) — both are done one level at
/// a time with one system call per level (.NET's CreateDirectory checks every parent first, and
/// its recursive Delete could itself run out of stack at these depths).
/// </summary>
public sealed class DeepTree : IDisposable
{
    public string Root { get; }
    public string Deepest { get; }

    public DeepTree(string parent, string rootName, int depth)
    {
        Root = Path.Combine(parent, rootName);
        Directory.CreateDirectory(Root);
        string current = Root;
        for (int i = 0; i < depth; i++)
        {
            current = Path.Combine(current, "d");
            if (!CreateDirectoryW(@"\\?\" + current, IntPtr.Zero))
                throw new IOException($"CreateDirectory failed at depth {i + 1}: error {Marshal.GetLastPInvokeError()}");
        }
        Deepest = current;
    }

    public void Dispose()
    {
        string stop = Path.GetDirectoryName(Root)!;
        for (string? dir = Deepest; dir is not null && dir.Length > stop.Length; dir = Path.GetDirectoryName(dir))
        {
            foreach (string file in Directory.GetFiles(dir))
                File.Delete(file);
            RemoveDirectoryW(@"\\?\" + dir);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveDirectoryW(string path);
}
