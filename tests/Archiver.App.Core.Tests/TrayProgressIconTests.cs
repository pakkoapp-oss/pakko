using FluentAssertions;

namespace Archiver.App.Core.Tests;

// T-F432: the tray icon is redrawn with a bar along its bottom while an operation runs.
public sealed class TrayProgressIconTests
{
    private const uint Base = 0xFF123456;

    private static uint[] Plain(int size) => Enumerable.Repeat(Base, size * size).ToArray();

    private static int Filled(uint[] pixels, int size, uint color)
    {
        int row = size - 1;
        return Enumerable.Range(0, size).Count(x => pixels[row * size + x] == color);
    }

    // --- Happy path ---

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 8)]
    [InlineData(100, 16)]
    public void Draw_FillsTheBarByThePercent(int percent, int filled)
    {
        uint[] pixels = TrayProgressIcon.Draw(Plain(16), 16, percent, TaskbarProgressState.Normal);

        Filled(pixels, 16, TrayProgressIcon.NormalColor).Should().Be(filled);
        Filled(pixels, 16, TrayProgressIcon.TrackColor).Should().Be(16 - filled);
    }

    [Theory]
    [InlineData(TaskbarProgressState.Normal, TrayProgressIcon.NormalColor)]
    [InlineData(TaskbarProgressState.Paused, TrayProgressIcon.PausedColor)]
    [InlineData(TaskbarProgressState.Error, TrayProgressIcon.ErrorColor)]
    public void Draw_ColoursTheBarByTheState(TaskbarProgressState state, uint color)
    {
        uint[] pixels = TrayProgressIcon.Draw(Plain(16), 16, 100, state);

        Filled(pixels, 16, color).Should().Be(16);
    }

    [Fact]
    public void Draw_Indeterminate_FillsTheWholeBar()
    {
        uint[] pixels = TrayProgressIcon.Draw(Plain(16), 16, 10, TaskbarProgressState.Indeterminate);

        Filled(pixels, 16, TrayProgressIcon.NormalColor).Should().Be(16);
    }

    [Fact]
    public void Draw_None_IsTheIconUnchanged()
    {
        TrayProgressIcon.Draw(Plain(16), 16, 70, TaskbarProgressState.None).Should().Equal(Plain(16));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 5)]
    [InlineData(42, 40)]
    [InlineData(99, 95)]
    [InlineData(100, 100)]
    public void Step_RoundsDownToFivePercent(int percent, int step)
    {
        TrayProgressIcon.Step(percent).Should().Be(step);
    }

    // --- Security & boundary ---

    [Theory]
    [InlineData(16, 2)]
    [InlineData(20, 2)]
    [InlineData(24, 3)]
    [InlineData(32, 4)]
    public void Draw_TouchesOnlyTheBarsRows(int size, int barRows)
    {
        uint[] pixels = TrayProgressIcon.Draw(Plain(size), size, 60, TaskbarProgressState.Normal);

        pixels.Take((size - barRows) * size).Should().OnlyContain(p => p == Base);
        pixels.Skip((size - barRows) * size).Should().NotContain(Base);
    }

    [Fact]
    public void Draw_OnePercent_ShowsAtLeastOnePixel()
    {
        uint[] pixels = TrayProgressIcon.Draw(Plain(16), 16, 1, TaskbarProgressState.Normal);

        Filled(pixels, 16, TrayProgressIcon.NormalColor).Should().Be(1);
    }

    [Fact]
    public void Draw_NinetyNinePercent_LeavesAPixelOfTrack()
    {
        uint[] pixels = TrayProgressIcon.Draw(Plain(16), 16, 99, TaskbarProgressState.Normal);

        Filled(pixels, 16, TrayProgressIcon.TrackColor).Should().Be(1);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(250, 16)]
    public void Draw_ClampsThePercent(int percent, int filled)
    {
        uint[] pixels = TrayProgressIcon.Draw(Plain(16), 16, percent, TaskbarProgressState.Normal);

        Filled(pixels, 16, TrayProgressIcon.NormalColor).Should().Be(filled);
    }

    // --- Misuse ---

    [Theory]
    [InlineData(15)]
    [InlineData(257)]
    public void Draw_ABufferOfTheWrongLength_Throws(int length)
    {
        Action draw = () => TrayProgressIcon.Draw(new uint[length], 16, 50, TaskbarProgressState.Normal);

        draw.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-16)]
    [InlineData(3)]
    public void Draw_ASizeTooSmallForABar_Throws(int size)
    {
        Action draw = () => TrayProgressIcon.Draw(new uint[Math.Max(0, size * size)], size, 50, TaskbarProgressState.Normal);

        draw.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Draw_LeavesTheBasePixelsAlone()
    {
        uint[] basePixels = Plain(16);

        TrayProgressIcon.Draw(basePixels, 16, 50, TaskbarProgressState.Normal);

        basePixels.Should().OnlyContain(p => p == Base);
    }
}
