using System.Runtime.InteropServices;

namespace Archiver.App.Core;

/// <summary>
/// T-F433: the App window's minimum size. <c>OverlappedPresenter</c> takes it in physical pixels, so
/// a fixed 900 x 520 shrank to 514 x 297 effective pixels at 175 % and the footer could be resized
/// out of the window. The floor is set in effective pixels and scaled by the window's DPI.
/// </summary>
public static partial class WindowFloor
{
    /// <summary>The floor in effective pixels: title bar, toolbar, the table's 160 px, a slice of options, the footer.</summary>
    public const int Width = 900;

    /// <inheritdoc cref="Width"/>
    public const int Height = 520;

    private const uint DefaultDpi = 96;

    /// <summary>
    /// The floor in physical pixels at <paramref name="dpi"/> (0, an invalid window, counts as 96),
    /// each side rounded up and never larger than a non-empty <paramref name="workArea"/>.
    /// </summary>
    public static (int Width, int Height) Physical(uint dpi, (int Width, int Height) workArea)
    {
        uint effectiveDpi = dpi == 0 ? DefaultDpi : dpi;
        return (Fit(Scale(Width, effectiveDpi), workArea.Width), Fit(Scale(Height, effectiveDpi), workArea.Height));
    }

    /// <summary>The window's size with each side grown to at least the floor's.</summary>
    public static (int Width, int Height) Grow((int Width, int Height) size, (int Width, int Height) floor) =>
        (Math.Max(size.Width, floor.Width), Math.Max(size.Height, floor.Height));

    /// <summary>The DPI of the monitor <paramref name="hwnd"/> is on; 0 for an invalid window.</summary>
    public static uint DpiOf(IntPtr hwnd) => GetDpiForWindow(hwnd);

    private static int Scale(int effective, uint dpi) => (int)Math.Ceiling(effective * (double)dpi / DefaultDpi);

    private static int Fit(int length, int available) => available > 0 ? Math.Min(length, available) : length;

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);
}
