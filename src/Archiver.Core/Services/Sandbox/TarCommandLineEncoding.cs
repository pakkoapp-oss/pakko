using System.Globalization;
using System.Runtime.InteropServices;
using Archiver.Core.Models;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F266: tar.exe (bsdtar) takes its command line through the ANSI code page, and the C runtime
/// converts it with best-fit mapping — a fullwidth quote (U+FF02) becomes '"' and breaks the
/// quoting of the argument it sits in ("WorstFit"). A string may reach tar.exe only if it
/// converts to the ANSI code page exactly (no best-fit, no default character) and back to itself.
/// Checked with the operating system's own tables, the ones the C runtime uses.
/// </summary>
internal static partial class TarCommandLineEncoding
{
    private const uint CpUtf8 = 65001;
    private const uint WcNoBestFitChars = 0x00000400;
    private const uint WcErrInvalidChars = 0x00000080;
    private const uint MbErrInvalidChars = 0x00000008;

    /// <summary>The ANSI code page tar.exe converts its command line with.</summary>
    public static uint AnsiCodePage => GetACP();

    /// <summary>True when <paramref name="value"/> reaches tar.exe unchanged.</summary>
    public static bool IsRepresentable(string value) => IsRepresentable(value, AnsiCodePage);

    internal static bool IsRepresentable(string value, uint codePage) => TryEncode(value, codePage) is not null;

    /// <summary>
    /// T-F283: <paramref name="lines"/> as the newline-terminated list tar.exe reads with "-T -",
    /// in the ANSI code page its names go through. Throws <see cref="TarArgumentEncodingException"/>
    /// for the first line that would not reach tar.exe unchanged.
    /// </summary>
    public static byte[] EncodeLines(IEnumerable<string> lines) => EncodeLines(lines, AnsiCodePage);

    internal static byte[] EncodeLines(IEnumerable<string> lines, uint codePage)
    {
        var result = new List<byte>();
        foreach (string line in lines)
        {
            // A line break would split one name into two list entries.
            byte[] bytes = (line.AsSpan().IndexOfAny('\r', '\n') < 0 ? TryEncode(line, codePage) : null)
                ?? throw new TarArgumentEncodingException(line, codePage);
            result.AddRange(bytes);
            result.Add((byte)'\n');
        }
        return [.. result];
    }

    // The exact bytes of value in codePage, or null when it would not convert back to itself.
    private static byte[]? TryEncode(string value, uint codePage)
    {
        if (value.Length == 0)
            return [];

        // UTF-8 allows neither the no-best-fit flag nor a used-default pointer; it only fails on
        // an unpaired surrogate, which the flag below turns into an error.
        bool utf8 = codePage == CpUtf8;
        uint flags = utf8 ? WcErrInvalidChars : WcNoBestFitChars;

        int byteCount = WideCharToMultiByte(codePage, flags, value, value.Length, null, 0, IntPtr.Zero, IntPtr.Zero);
        if (byteCount <= 0)
            return null;

        byte[] bytes = new byte[byteCount];
        int usedDefault = 0;
        int written = utf8
            ? WideCharToMultiByte(codePage, flags, value, value.Length, bytes, bytes.Length, IntPtr.Zero, IntPtr.Zero)
            : WideCharToMultiByteUsedDefault(codePage, flags, value, value.Length, bytes, bytes.Length, IntPtr.Zero, out usedDefault);
        if (written != byteCount || usedDefault != 0)
            return null;

        int charCount = MultiByteToWideChar(codePage, MbErrInvalidChars, bytes, bytes.Length, null, 0);
        if (charCount != value.Length)
            return null;

        char[] roundTrip = new char[charCount];
        bool exact = MultiByteToWideChar(codePage, MbErrInvalidChars, bytes, bytes.Length, roundTrip, roundTrip.Length) == charCount
            && value.AsSpan().SequenceEqual(roundTrip);
        return exact ? bytes : null;
    }

    /// <summary>Throws <see cref="TarArgumentEncodingException"/> for the first argument that would not reach tar.exe unchanged.</summary>
    public static void EnsureRepresentable(IEnumerable<string> arguments)
    {
        string? unrepresentable = arguments.FirstOrDefault(argument => !IsRepresentable(argument));
        if (unrepresentable is not null)
            throw new TarArgumentEncodingException(unrepresentable, AnsiCodePage);
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetACP();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int WideCharToMultiByte(
        uint codePage, uint flags, string wideChars, int wideCharCount,
        [Out] byte[]? multiByte, int multiByteCount, IntPtr defaultChar, IntPtr usedDefaultChar);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true, EntryPoint = "WideCharToMultiByte")]
    private static partial int WideCharToMultiByteUsedDefault(
        uint codePage, uint flags, string wideChars, int wideCharCount,
        [Out] byte[] multiByte, int multiByteCount, IntPtr defaultChar, out int usedDefaultChar);

    // StringMarshalling.Utf16 matters: a char[] must go through as UTF-16, one wide char per char.
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int MultiByteToWideChar(
        uint codePage, uint flags, [In] byte[] multiByte, int multiByteCount, [Out] char[]? wideChars, int wideCharCount);
}

/// <summary>
/// A name that tar.exe would receive altered (T-F266) — refused before tar.exe runs. An
/// <see cref="IOException"/>, so every tar call site reports it as an ordinary per-archive error.
/// </summary>
internal sealed class TarArgumentEncodingException(string argument, uint codePage) // NOSONAR: S3871 — deliberately internal, always caught and turned into an ArchiveError (same as SandboxSetupException)
    : IOException(Describe(argument, codePage).English), ICoreTextSource
{
    public CoreText Text { get; } = Describe(argument, codePage);

    internal static CoreText Describe(string argument, uint codePage) =>
        CoreMessages.Text(MessageCode.TarNameNotRepresentable, argument, codePage.ToString(CultureInfo.InvariantCulture));
}
