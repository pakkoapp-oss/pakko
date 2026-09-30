using System.Runtime.InteropServices;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F255: Explorer's password dialog read the edit control into a fixed 256-char buffer, so a
// longer password was cut without notice and reported as wrong. Uses a real (never shown) EDIT
// control: WM_GETTEXT/WM_SETTEXT are sent on this thread, so no message loop is needed.
public sealed partial class PasswordDialogReadTextTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(300)]
    [InlineData(4096)]
    public void ReadEditText_ReturnsTheWholeText(int length)
    {
        string password = string.Concat(Enumerable.Range(0, length).Select(i => (char)('a' + i % 26)));
        IntPtr edit = CreateWindowExW(0, "EDIT", null, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        edit.Should().NotBe(IntPtr.Zero);
        try
        {
            SendMessageW(edit, EmLimitText, 0, 0); // lift the edit control's own 30,000 default only for the test's sake
            SetWindowTextW(edit, password).Should().BeTrue();

            PasswordDialog.ReadEditText(edit).Should().Be(password);
        }
        finally
        {
            DestroyWindow(edit);
        }
    }

    private const int EmLimitText = 0x00C5;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowExW(int exStyle, string className, string? windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowTextW(IntPtr hWnd, string text);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr hWnd, int msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hWnd);
}
