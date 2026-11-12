using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F511 PR 6: a Windows error code is read from an exception in one place, CoreMessages, next to
// the table that names it. Each classifier is asked of the exception itself, not of what it wraps.
public sealed class CoreMessagesClassifierTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void IsAlreadyExists_CreateNewOverAFile_True()
    {
        string path = Path.Combine(_temp.Path, "a.txt");
        File.WriteAllText(path, "x");

        IOException ex = Assert.Throws<IOException>(() => new FileStream(path, FileMode.CreateNew).Dispose());

        CoreMessages.IsAlreadyExists(ex).Should().BeTrue();
        CoreMessages.IsSharingViolation(ex).Should().BeFalse();
        CoreMessages.IsDiskFull(ex).Should().BeFalse();
    }

    [Fact]
    public void IsSharingViolation_AFileHeldWithNoSharing_True()
    {
        string path = Path.Combine(_temp.Path, "a.txt");
        using var held = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        IOException ex = Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.Read).Dispose());

        CoreMessages.IsSharingViolation(ex).Should().BeTrue();
        CoreMessages.IsAlreadyExists(ex).Should().BeFalse();
    }

    [Theory]
    [InlineData(unchecked((int)0x80070070), true)]  // ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070027), true)]  // ERROR_HANDLE_DISK_FULL
    [InlineData(unchecked((int)0x80040070), false)] // the same low bits in another facility
    [InlineData(unchecked((int)0x80131620), false)] // COR_E_IO, a plain IOException
    public void IsDiskFull_OnlyWindowsDiskFullCodes(int hResult, bool expected) =>
        CoreMessages.IsDiskFull(new IOException("x", hResult)).Should().Be(expected);

    [Fact]
    public void Classifiers_AskTheExceptionItself_NotWhatItWraps()
    {
        var wrapped = new IOException("outer", new IOException("inner", unchecked((int)0x80070070)));

        CoreMessages.IsDiskFull(wrapped).Should().BeFalse();
    }

    [Theory]
    [InlineData(unchecked((int)0x80070020))] // ERROR_SHARING_VIOLATION
    [InlineData(unchecked((int)0x80070021))] // ERROR_LOCK_VIOLATION
    public void IsSharingViolation_BothCodes(int hResult) =>
        CoreMessages.IsSharingViolation(new IOException("x", hResult)).Should().BeTrue();

    [Theory]
    [InlineData(unchecked((int)0x80070050))] // ERROR_FILE_EXISTS
    [InlineData(unchecked((int)0x800700B7))] // ERROR_ALREADY_EXISTS
    public void IsAlreadyExists_BothCodes(int hResult) =>
        CoreMessages.IsAlreadyExists(new IOException("x", hResult)).Should().BeTrue();
}
