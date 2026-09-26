using System.Buffers.Binary;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Archiver.OperationUi.Protocol;

/// <summary>
/// A frame is a 4-byte little-endian payload length, then the message as UTF-8 JSON.
/// </summary>
public static class FrameCodec
{
    /// <summary>
    /// Sized from the largest legal message, not to limit content: a 32,767-char path in a conflict,
    /// or a result listing ten of them, is well under it. The cap only stops a garbage length field
    /// from allocating gigabytes.
    /// </summary>
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    public const int ProtocolVersion = 1;

    private const int HeaderBytes = 4;

    // Relaxed escaping writes Cyrillic as UTF-8 (2 bytes) instead of \uXXXX (6); both ends are
    // this package's own processes, and the JSON is never embedded in HTML.
    private static readonly JsonTypeInfo<ProtocolMessage> TypeInfo =
        new ProtocolJsonContext(new JsonSerializerOptions(ProtocolJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).ProtocolMessage;

    /// <summary>Throws <see cref="ProtocolException"/> if the message is larger than <see cref="MaxFrameBytes"/>.</summary>
    public static byte[] Encode(ProtocolMessage message)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, TypeInfo);
        if (payload.Length > MaxFrameBytes)
            throw new ProtocolException($"Message is {payload.Length} bytes; the limit is {MaxFrameBytes}.");

        var frame = new byte[HeaderBytes + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderBytes));
        return frame;
    }

    /// <summary>
    /// Reads one message; null at a clean end of stream (the other side closed between frames).
    /// On an anonymous pipe a blocked read may not wake on <paramref name="cancellationToken"/> —
    /// dispose the stream, or let the other side exit, to end it.
    /// </summary>
    public static async Task<ProtocolMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderBytes];
        int headerRead = await ReadFullyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (headerRead == 0)
            return null;
        if (headerRead < HeaderBytes)
            throw new ProtocolException("The stream ended inside a frame header.");

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrameBytes)
            throw new ProtocolException($"Invalid frame length {length}.");

        var payload = new byte[length];
        if (await ReadFullyAsync(stream, payload, cancellationToken).ConfigureAwait(false) < length)
            throw new ProtocolException("The stream ended inside a frame.");

        return Decode(payload);
    }

    internal static ProtocolMessage Decode(ReadOnlySpan<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize(payload, TypeInfo)
                ?? throw new ProtocolException("The frame holds no message.");
        }
        // No inner exception: its text can quote the payload.
        catch (JsonException)
        {
            throw new ProtocolException("The frame is not a valid message.");
        }
        catch (NotSupportedException)
        {
            throw new ProtocolException("The frame is not a valid message.");
        }
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }
}
