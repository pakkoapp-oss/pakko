using System.Security.Cryptography;

namespace Archiver.Core.Services.Zip.Decryption;

/// <summary>
/// WinZip AE's AES-CTR keystream: a 128-bit little-endian counter starting at 1, keystream from a
/// plain AES-ECB encryptor (the standard NIST SP 800-38A construction; .NET has no named CTR mode).
/// CTR is symmetric, so the same XOR serves decryption (<see cref="WinZipAesCtrStream"/>) and
/// encryption (T-F193, <c>WinZipAesEncryptStream</c>). Generates keystream for many blocks per
/// encryptor call rather than one block at a time.
/// </summary>
internal sealed class AesCtrKeystream : IDisposable
{
    private const int BlockSize = 16;
    private const int MaxBlocksPerBatch = 4096; // 64 KiB of keystream per ECB call

    private readonly Aes _aes;
    private readonly ICryptoTransform _encryptor;
    private readonly byte[] _counter = new byte[BlockSize];
    private readonly byte[] _counterBatch;
    private readonly byte[] _keystream;
    private int _keystreamOffset;

    // T-F504: a batch no larger than the data needs - a small entry built 128 KiB of keystream
    // buffers. More bytes than expected only refill again; the keystream does not depend on it.
    public AesCtrKeystream(byte[] key, long expectedBytes = long.MaxValue)
    {
        int blocksPerBatch = (int)Math.Clamp(expectedBytes / BlockSize + 1, 1, MaxBlocksPerBatch);
        _counterBatch = new byte[BlockSize * blocksPerBatch];
        _keystream = new byte[BlockSize * blocksPerBatch];
        _keystreamOffset = _keystream.Length;
        _aes = Aes.Create();
        _aes.Key = key;
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
        _encryptor = _aes.CreateEncryptor();
        _counter[0] = 1;
    }

    /// <summary>Writes <paramref name="input"/> XOR keystream into <paramref name="output"/> (may be the same span).</summary>
    public void Apply(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (output.Length < input.Length)
            throw new ArgumentException("Output is shorter than input.", nameof(output));

        for (int i = 0; i < input.Length; i++)
        {
            if (_keystreamOffset >= _keystream.Length)
                RefillKeystream();
            output[i] = (byte)(input[i] ^ _keystream[_keystreamOffset++]);
        }
    }

    private void RefillKeystream()
    {
        for (int block = 0; block < _counterBatch.Length / BlockSize; block++)
        {
            Buffer.BlockCopy(_counter, 0, _counterBatch, block * BlockSize, BlockSize);
            IncrementCounter();
        }
        _encryptor.TransformBlock(_counterBatch, 0, _counterBatch.Length, _keystream, 0);
        _keystreamOffset = 0;
    }

    private void IncrementCounter()
    {
        for (int i = 0; i < _counter.Length; i++)
        {
            if (++_counter[i] != 0)
                break;
        }
    }

    public void Dispose()
    {
        _encryptor.Dispose();
        _aes.Dispose();
        // T-F244 item 3: buffered keystream is key-derived material too.
        CryptographicOperations.ZeroMemory(_keystream);
        CryptographicOperations.ZeroMemory(_counterBatch);
    }
}
