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
    public void IsPreviewable_NonAllowlistedExtension_ReturnsFalse(string entryName)
    {
        Assert.False(PreviewPolicy.IsPreviewable(entryName));
    }
}
