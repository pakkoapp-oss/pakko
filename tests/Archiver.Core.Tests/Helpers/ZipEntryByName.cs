using System.Text;
using Archiver.Core.Services.Zip;
using Archiver.Core.Services.Zip.Decryption;

namespace Archiver.Core.Tests.Helpers;

/// <summary>
/// T-F543: one entry by its decoded name, through the catalog the product reads with - Core keeps no
/// name-based reader of its own (one walked the whole directory with no name budget).
/// </summary>
internal static class ZipEntryByName
{
    public static LocatedZipEntry Locate(string zipPath, string entryFullName)
    {
        using var catalog = ZipCatalog.Open(zipPath, ZipNameCodePages.Current());
        foreach ((string name, LocatedZipEntry located) in catalog.Located())
        {
            if (name == entryFullName)
                return located;
        }
        throw new FileNotFoundException($"Entry not found in ZIP central directory: {entryFullName}");
    }

    /// <summary>Opens an encrypted entry; the returned stream owns the file it reads.</summary>
    public static (EncryptedZipReadResult Result, Stream? Content) TryOpen(
        string zipPath, string entryFullName, string password, Encoding? passwordEncoding = null)
    {
        LocatedZipEntry located = Locate(zipPath, entryFullName);
        if (!located.GeneralPurposeEncryptedBit)
            throw new InvalidOperationException($"'{entryFullName}' is not encrypted.");
        FileStream file = File.OpenRead(zipPath);
        try
        {
            (EncryptedZipReadResult result, Stream? content) = EncryptedZipEntryReader.TryOpen(file, located, password, passwordEncoding);
            if (content is null)
            {
                file.Dispose();
                return (result, null);
            }
            return (result, new Owning(content, file));
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private sealed class Owning(Stream content, FileStream file) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => content.Length;
        public override long Position { get => content.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => content.Read(buffer, offset, count);
        public override void Flush() { /* read-only */ }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                content.Dispose();
                file.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
