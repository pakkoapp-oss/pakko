using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Archiver.App.Core;
using H.NotifyIcon;

namespace Archiver.App;

/// <summary>
/// T-F432: puts <see cref="TrayProgressIcon"/>'s bar on the tray icon. The HICONs made here are ours:
/// H.NotifyIcon 2.3.2's <c>TrayIcon.UpdateIcon</c> only keeps the handle (and shows it again when
/// Explorer restarts), never destroys it, so the one on show lives until the next replaces it.
/// Called on the window's UI thread only.
/// </summary>
internal sealed unsafe partial class TrayProgressRenderer : IDisposable
{
    private const string Name = "Pakko";
    private readonly TaskbarIcon _tray;
    private readonly IntPtr _hwnd;
    private uint[]? _basePixels;
    private int _baseSize;
    private IntPtr _current;
    private (TaskbarProgressState State, int Step) _shown = (TaskbarProgressState.None, 0);

    public TrayProgressRenderer(TaskbarIcon tray, IntPtr hwnd)
    {
        _tray = tray;
        _hwnd = hwnd;
    }

    public void Show(TaskbarProgressState state, int percent)
    {
        (TaskbarProgressState State, int Step) wanted = (state, state == TaskbarProgressState.None ? 0 : TrayProgressIcon.Step(percent));
        if (wanted == _shown)
            return;
        if (state == TaskbarProgressState.None)
        {
            if (Restore())
                _shown = wanted;
            return;
        }

        int size = SmallIconSize();
        uint[]? basePixels = BasePixels(size);
        if (basePixels is null)
            return; // the plain icon stays; the window's bar and the taskbar button still show the run
        IntPtr icon = CreateIcon(TrayProgressIcon.Draw(basePixels, size, wanted.Step, state), size);
        if (icon == IntPtr.Zero)
            return;
        if (!_tray.TrayIcon.UpdateIcon(icon))
        {
            DestroyIcon(icon);
            return;
        }
        DestroyCurrent();
        _current = icon;
        _shown = wanted;
        _tray.ToolTipText = Name + ": " + (Math.Clamp(percent, 0, 100) / 100.0).ToString("P0", CultureInfo.CurrentCulture);
    }

    public void Dispose() => _ = Restore(); // a failure leaks one HICON at exit, freed with the process

    // False while the tray could not take the plain icon back (Explorer down): the library still
    // holds Pakko's HICON and shows it again on TaskbarCreated, so it must stay alive; the next
    // Show(None) or Dispose tries again.
    private bool Restore()
    {
        if (_current == IntPtr.Zero)
            return true;
        // The library's own icon, made from IconSource; the Icon property keeps it unchanged.
        if (!_tray.UpdateIcon(_tray.Icon))
            return false;
        _tray.ToolTipText = Name;
        DestroyCurrent();
        return true;
    }

    private void DestroyCurrent()
    {
        if (_current != IntPtr.Zero)
            DestroyIcon(_current);
        _current = IntPtr.Zero;
    }

    private int SmallIconSize()
    {
        uint dpi = WindowFloor.DpiOf(_hwnd);
        return Math.Max(TrayProgressIcon.MinimumSize, GetSystemMetricsForDpi(SmallIconMetric, dpi));
    }

    // The packaged .ico at the tray's size, read once per size (a DPI change asks for another).
    private uint[]? BasePixels(int size)
    {
        if (_basePixels is not null && _baseSize == size)
            return _basePixels;
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.ico");
        IntPtr icon = LoadImageW(IntPtr.Zero, path, ImageIcon, size, size, LoadFromFile);
        if (icon == IntPtr.Zero)
            return null;
        try
        {
            _basePixels = ReadPixels(icon, size);
            _baseSize = size;
            return _basePixels;
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    private static uint[]? ReadPixels(IntPtr icon, int size)
    {
        if (!GetIconInfo(icon, out IconInfo info))
            return null;
        try
        {
            var pixels = new uint[size * size];
            BitmapInfoHeader header = TopDown32(size);
            IntPtr screen = GetDC(IntPtr.Zero);
            try
            {
                return GetDIBits(screen, info.Color, 0, (uint)size, pixels, ref header, DibRgbColors) == size ? pixels : null;
            }
            finally
            {
                _ = ReleaseDC(IntPtr.Zero, screen); // 1 or 0; nothing to do about a 0
            }
        }
        finally
        {
            DeleteObject(info.Mask);
            DeleteObject(info.Color);
        }
    }

    private static IntPtr CreateIcon(uint[] pixels, int size)
    {
        BitmapInfoHeader header = TopDown32(size);
        IntPtr color = CreateDIBSection(IntPtr.Zero, ref header, DibRgbColors, out IntPtr bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero)
            return IntPtr.Zero;
        // A 32-bit colour bitmap carries its own alpha; the mask is required but not used.
        IntPtr mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
        try
        {
            if (mask == IntPtr.Zero)
                return IntPtr.Zero;
            pixels.CopyTo(new Span<uint>((void*)bits, pixels.Length));
            var info = new IconInfo { IsIcon = 1, Mask = mask, Color = color };
            return CreateIconIndirect(ref info);
        }
        finally
        {
            DeleteObject(color);
            if (mask != IntPtr.Zero)
                DeleteObject(mask);
        }
    }

    private static BitmapInfoHeader TopDown32(int size) => new()
    {
        Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
        Width = size,
        Height = -size,
        Planes = 1,
        BitCount = 32,
    };

    private const int SmallIconMetric = 49; // SM_CXSMICON
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x10;
    private const uint DibRgbColors = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public IntPtr Mask;
        public IntPtr Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr LoadImageW(IntPtr instance, string name, uint type, int width, int height, uint load);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(IntPtr icon, out IconInfo info);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CreateIconIndirect(ref IconInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] uint[] bits, ref BitmapInfoHeader info, uint usage);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr gdiObject);
}
