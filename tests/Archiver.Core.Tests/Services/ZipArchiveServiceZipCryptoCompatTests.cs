using System.Text;
using Archiver.Core.Models;
using Archiver.Core.Services;
using Archiver.Core.Tests.Helpers;
using FluentAssertions;

namespace Archiver.Core.Tests.Services;

// T-F243 item 2: with a data descriptor (bit 3) the ZipCrypto check byte is the high byte of the
// file time (Info-ZIP; 7-Zip ZipHandler.cpp checks it the same way), not of the CRC-32 — the
// CRC is not known when the header is written. Checking the CRC byte rejected the right password.
public sealed class ZipArchiveServiceZipCryptoCompatTests : IDisposable
{
    private const string Password = "testpassword";
    private readonly ZipArchiveService _sut = new();
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static Func<PasswordPromptInfo, Task<PasswordDecision>> Fixed(string password) =>
        _ => Task.FromResult(new PasswordDecision { Password = password });

    private async Task<(ArchiveResult Result, string Dest)> ExtractAsync(string zip, string password)
    {
        string dest = Path.Combine(_temp.Path, "out-" + Guid.NewGuid().ToString("N"));
        var result = await _sut.ExtractAsync(new ExtractOptions
        {
            ArchivePaths = [zip],
            DestinationFolder = dest,
            Mode = ExtractMode.SingleFolder,
            ResolvePasswordAsync = Fixed(password),
        });
        return (result, dest);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExtractAsync_ZipCrypto_RightPassword_Extracts(bool dataDescriptor)
    {
        string zip = ZipCryptoFixture.Write(Path.Combine(_temp.Path, $"zc{dataDescriptor}.zip"), "a.txt",
            Encoding.ASCII.GetBytes("hello zipcrypto"), Encoding.ASCII.GetBytes(Password), dataDescriptor);

        var (result, dest) = await ExtractAsync(zip, Password);

        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        File.ReadAllText(Path.Combine(dest, "a.txt")).Should().Be("hello zipcrypto");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestAsync_ZipCrypto_RightPassword_Passes(bool dataDescriptor)
    {
        string zip = ZipCryptoFixture.Write(Path.Combine(_temp.Path, $"zct{dataDescriptor}.zip"), "a.txt",
            Encoding.ASCII.GetBytes("hello zipcrypto"), Encoding.ASCII.GetBytes(Password), dataDescriptor);

        var result = await _sut.TestAsync([zip], resolvePasswordAsync: Fixed(Password));

        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public async Task ExtractAsync_DescriptorEntry_WrongPassword_Fails()
    {
        string zip = ZipCryptoFixture.Write(Path.Combine(_temp.Path, "zcw.zip"), "a.txt",
            Encoding.ASCII.GetBytes("hello zipcrypto"), Encoding.ASCII.GetBytes(Password), dataDescriptor: true);

        var (result, dest) = await ExtractAsync(zip, "not-the-password");

        result.Success.Should().BeFalse();
        File.Exists(Path.Combine(dest, "a.txt")).Should().BeFalse();
    }
}
