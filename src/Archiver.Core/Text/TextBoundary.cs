using System.Text;

namespace Archiver.Core.Text;

/// <summary>
/// T-F467: one text boundary's code page, as <see cref="TextBoundaries"/> chose it. <see cref="Strict"/>
/// throws on a character or byte the page cannot carry; <see cref="Lenient"/> puts the page's
/// replacement in its place. A boundary that decides something reads strictly, or guards the
/// lenient result (T-F465's byte guard).
/// </summary>
public sealed record TextBoundary(int CodePage)
{
    /// <summary>The page's encoding; unmappable input becomes its replacement character.</summary>
    public Encoding Lenient => SystemCodePages.For(CodePage);

    /// <summary>The page's encoding; unmappable input throws.</summary>
    public Encoding Strict => SystemCodePages.ForStrict(CodePage);
}
