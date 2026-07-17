using Archiver.Core.Services;
using Xunit;

namespace Archiver.Core.Tests.Services;

public class PreviewPolicyTests
{
    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPG")]
    [InlineData("photo.jpeg")]
    [InlineData("icon.png")]
    [InlineData("anim.gif")]
    [InlineData("scan.bmp")]
    [InlineData("pic.webp")]
    [InlineData("readme.txt")]
    [InlineData("notes.md")]
    [InlineData("app.log")]
    [InlineData("config.ini")]
    [InlineData("data.csv")]
    [InlineData("data.json")]
    [InlineData("data.xml")]
    [InlineData("data.yaml")]
    [InlineData("data.yml")]
    [InlineData("sub/folder/readme.TXT")]
    [InlineData("clip.mp4")]
    [InlineData("clip.MP4")]
    [InlineData("clip.m4v")]
    [InlineData("clip.mkv")]
    [InlineData("clip.avi")]
    [InlineData("clip.mov")]
    [InlineData("clip.wmv")]
    [InlineData("clip.webm")]
    [InlineData("song.mp3")]
    [InlineData("song.wav")]
    [InlineData("song.flac")]
    [InlineData("song.ogg")]
    [InlineData("song.m4a")]
    [InlineData("song.aac")]
    public void IsPreviewable_AllowlistedExtension_ReturnsTrue(string entryName)
    {
        Assert.True(PreviewPolicy.IsPreviewable(entryName));
    }

    [Theory]
    [InlineData("app.exe")]
    [InlineData("resume.docx")]
    [InlineData("nested.zip")]
    [InlineData("shortcut.lnk")]
    [InlineData("script.ps1")]
    [InlineData("noextension")]
    // T-F109: PDF deliberately excluded despite being a common "safe-looking" format — it can
    // embed JavaScript executed by some readers, unlike every other allowlisted type here. See
    // SECURITY.md/DECISIONS.md's T-F109 entry.
    [InlineData("document.pdf")]
    public void IsPreviewable_NonAllowlistedExtension_ReturnsFalse(string entryName)
    {
        Assert.False(PreviewPolicy.IsPreviewable(entryName));
    }
}
