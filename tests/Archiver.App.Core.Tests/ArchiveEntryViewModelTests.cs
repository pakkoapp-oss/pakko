using Archiver.App.Core;
using FluentAssertions;
using Xunit;

namespace Archiver.App.Core.Tests;

public class ArchiveEntryViewModelTests
{
    [Fact]
    public void CompressedSizeDisplay_Folder_IsEmpty()
    {
        var entry = new ArchiveEntryViewModel
        {
            FullPath = "dir",
            Name = "dir",
            IsFolder = true,
            Size = 0,
            CompressedSize = 100,
        };

        entry.CompressedSizeDisplay.Should().BeEmpty();
    }

    [Fact]
    public void CompressedSizeDisplay_ZeroCompressedSize_IsEmpty()
    {
        // Real for every tar-routed format (RAR/7z/tar.*) — TarProcessService's listing path
        // never populates a per-entry compressed size.
        var entry = new ArchiveEntryViewModel
        {
            FullPath = "file.txt",
            Name = "file.txt",
            IsFolder = false,
            Size = 500,
            CompressedSize = 0,
        };

        entry.CompressedSizeDisplay.Should().BeEmpty();
    }

    [Fact]
    public void CompressedSizeDisplay_PositiveCompressedSize_IsFormatted()
    {
        var entry = new ArchiveEntryViewModel
        {
            FullPath = "file.txt",
            Name = "file.txt",
            IsFolder = false,
            Size = 2048,
            CompressedSize = 512,
        };

        entry.CompressedSizeDisplay.Should().Be("512 bytes");
    }

    [Fact]
    public void CrcDisplay_Folder_IsEmpty()
    {
        var entry = new ArchiveEntryViewModel
        {
            FullPath = "dir",
            Name = "dir",
            IsFolder = true,
            Crc32 = 0xDEADBEEF,
        };

        entry.CrcDisplay.Should().BeEmpty();
    }

    [Fact]
    public void CrcDisplay_NullCrc_IsEmpty()
    {
        // Real for every tar-routed format (RAR/7z/tar.*) — there is no per-entry CRC-32 concept
        // for tar.exe-listed formats.
        var entry = new ArchiveEntryViewModel
        {
            FullPath = "file.txt",
            Name = "file.txt",
            IsFolder = false,
            Crc32 = null,
        };

        entry.CrcDisplay.Should().BeEmpty();
    }

    [Fact]
    public void CrcDisplay_ZeroCrc_IsFormatted()
    {
        // 0 is a legitimate CRC-32 (e.g. an empty file) — must NOT be treated the same as "not
        // available" (unlike CompressedSizeDisplay's <= 0 guard, which is safe because 0 always
        // means "no data" there).
        var entry = new ArchiveEntryViewModel
        {
            FullPath = "empty.txt",
            Name = "empty.txt",
            IsFolder = false,
            Crc32 = 0,
        };

        entry.CrcDisplay.Should().Be("00000000");
    }

    [Fact]
    public void CrcDisplay_PositiveCrc_IsFormattedAsUppercaseHex()
    {
        var entry = new ArchiveEntryViewModel
        {
            FullPath = "file.txt",
            Name = "file.txt",
            IsFolder = false,
            Crc32 = 0xDEADBEEF,
        };

        entry.CrcDisplay.Should().Be("DEADBEEF");
    }

    [Fact]
    public void Icon_Folder_ReturnsFolderGlyph()
    {
        var entry = new ArchiveEntryViewModel
        {
            FullPath = "dir",
            Name = "dir",
            IsFolder = true,
        };

        entry.Icon.Should().Be("\uE8B7");
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("clip.mp4")]
    [InlineData("song.mp3")]
    [InlineData("readme.txt")]
    public void Icon_PreviewableFile_ReturnsViewGlyph(string name)
    {
        var entry = new ArchiveEntryViewModel
        {
            FullPath = name,
            Name = name,
            IsFolder = false,
        };

        entry.Icon.Should().Be("\uE890");
    }

    [Theory]
    [InlineData("app.exe")]
    [InlineData("resume.docx")]
    [InlineData("document.pdf")]
    public void Icon_NonPreviewableFile_ReturnsHideGlyph(string name)
    {
        var entry = new ArchiveEntryViewModel
        {
            FullPath = name,
            Name = name,
            IsFolder = false,
        };

        entry.Icon.Should().Be("\uED1A");
    }

    [Theory]
    [InlineData("nested.zip")]
    [InlineData("nested.tar.gz")]
    [InlineData("nested.7z")]
    public void Icon_NestedArchiveWithinDepthLimit_ReturnsViewGlyph(string name)
    {
        // T-F98/T-F110: an archive found inside the currently browsed archive drills in
        // transparently, so it reads the same as a previewable file unless the depth limit
        // blocks it (see the next test).
        var entry = new ArchiveEntryViewModel
        {
            FullPath = name,
            Name = name,
            IsFolder = false,
            NestedDepthLimitReached = false,
        };

        entry.Icon.Should().Be("\uE890");
    }

    [Fact]
    public void Icon_NestedArchiveExceedingDepthLimit_ReturnsHideGlyph()
    {
        var entry = new ArchiveEntryViewModel
        {
            FullPath = "nested.zip",
            Name = "nested.zip",
            IsFolder = false,
            NestedDepthLimitReached = true,
        };

        entry.Icon.Should().Be("\uED1A");
    }
}
