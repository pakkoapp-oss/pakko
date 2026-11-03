namespace Archiver.App.Core;

/// <summary>
/// T-F432: the tray icon with a progress bar along its bottom rows. Pixels are 32-bit ARGB, top row
/// first, as <c>GetDIBits</c> returns them for a top-down DIB; only the bar's rows are written, so
/// the icon above it stays as drawn. Opaque colours, so straight and premultiplied alpha agree.
/// </summary>
public static class TrayProgressIcon
{
    /// <summary>The smallest icon that still leaves rows above a two-row bar.</summary>
    public const int MinimumSize = 8;

    /// <summary>How far the percent moves before the icon is redrawn.</summary>
    public const int StepPercent = 5;

    /// <summary>The colours of the taskbar's own bars (green, yellow, red) and a dark track.</summary>
    public const uint NormalColor = 0xFF06B025;

    /// <inheritdoc cref="NormalColor"/>
    public const uint PausedColor = 0xFFDAA520;

    /// <inheritdoc cref="NormalColor"/>
    public const uint ErrorColor = 0xFFD83B01;

    /// <inheritdoc cref="NormalColor"/>
    public const uint TrackColor = 0xFF404040;

    /// <summary>The percent rounded down to <see cref="StepPercent"/>: the icon is redrawn only when this changes.</summary>
    public static int Step(int percent) => Math.Clamp(percent, 0, 100) / StepPercent * StepPercent;

    /// <summary>A copy of <paramref name="basePixels"/> (<paramref name="size"/> squared) with the bar drawn for <paramref name="state"/>.</summary>
    public static uint[] Draw(ReadOnlySpan<uint> basePixels, int size, int percent, TaskbarProgressState state)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, MinimumSize);
        if (basePixels.Length != size * size)
            throw new ArgumentException($"Expected {size * size} pixels for a {size} x {size} icon, got {basePixels.Length}.", nameof(basePixels));

        uint[] pixels = basePixels.ToArray();
        uint fill;
        int filled;
        switch (state)
        {
            case TaskbarProgressState.None:
                return pixels;
            case TaskbarProgressState.Indeterminate:
                (fill, filled) = (NormalColor, size);
                break;
            case TaskbarProgressState.Normal:
                (fill, filled) = (NormalColor, FilledColumns(size, percent));
                break;
            case TaskbarProgressState.Paused:
                (fill, filled) = (PausedColor, FilledColumns(size, percent));
                break;
            case TaskbarProgressState.Error:
                (fill, filled) = (ErrorColor, FilledColumns(size, percent));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Not a taskbar progress state.");
        }

        int barRows = BarRows(size);
        for (int row = size - barRows; row < size; row++)
        {
            Span<uint> line = pixels.AsSpan(row * size, size);
            line[..filled].Fill(fill);
            line[filled..].Fill(TrackColor);
        }
        return pixels;
    }

    /// <summary>The bar's height: an eighth of the icon, at least two rows.</summary>
    public static int BarRows(int size) => Math.Max(2, size / 8);

    // Any progress shows at least one column, and anything short of 100 leaves one of track.
    private static int FilledColumns(int size, int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        int columns = (int)Math.Round(size * clamped / 100.0, MidpointRounding.AwayFromZero);
        if (clamped > 0 && columns == 0)
            return 1;
        if (clamped < 100 && columns == size)
            return size - 1;
        return columns;
    }
}
