using System.Runtime.InteropServices;

namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// T-F266: tar.exe (bsdtar) takes its command line through the ANSI code page, and the C runtime
/// converts it with best-fit mapping — a fullwidth quote (U+FF02) becomes '"' and breaks the
/// quoting of the argument it sits in ("WorstFit"). A string may reach tar.exe only if it
/// converts to the ANSI code page exactly (no best-fit, no default character) and back to itself.
/// Checked with the operating system's own tables, the ones the C runtime uses.
/// </summary>
internal static class TarCommandLineEncoding
{
    private const uint CpUtf8 = 65001;
    private const uint WcNoBestFitChars = 0x00000400;
    private const uint WcErrInvalidChars = 0x00000080;
    private const uint MbErrInvalidChars = 0x00000008;

    /// <summary>The ANSI code page tar.exe converts its command line with.</summary>
    public static uint AnsiCodePage => GetACP();

    /// <summary>True when <paramref name="value"/> reaches tar.exe unchanged.</summary>
    public static bool IsRepresentable(string value) => IsRepresentable(value, AnsiCodePage);

    internal static bool IsRepresentable(string value, uint codePage)
    {
        if (value.Length == 0)
            return true;

        // UTF-8 allows neither the no-best-fit flag nor a used-default pointer; it only fails on
        // an unpaired surrogate, which the flag below turns into an error.
        bool utf8 = codePage == CpUtf8;
        uint flags = utf8 ? WcErrInvalidChars : WcNoBestFitChars;

        int byteCount = WideCharToMultiByte(codePage, flags, value, value.Length, null, 0, IntPtr.Zero, IntPtr.Zero);
        if (byteCount <= 0)
            return false;

        var bytes = new byte[byteCount];
        int usedDefault = 0;
        int written = utf8
            ? WideCharToMultiByte(codePage, flags, value, value.Length, bytes, bytes.Length, IntPtr.Zero, IntPtr.Zero)
            : WideCharToMultiByteUsedDefault(codePage, flags, value, value.Length, bytes, bytes.Length, IntPtr.Zero, out usedDefault);
        if (written != byteCount || usedDefault != 0)
            return false;

        int charCount = MultiByteToWideChar(codePage, MbErrInvalidChars, bytes, bytes.Length, null, 0);
        if (charCount != value.Length)
            return false;

        var roundTrip = new char[charCount];
        return MultiByteToWideChar(codePage, MbErrInvalidChars, bytes, bytes.Length, roundTrip, roundTrip.Length) == charCount
            && value.AsSpan().SequenceEqual(roundTrip);
    }

    /// <summary>Throws <see cref="TarArgumentEncodingException"/> for the first argument that would not reach tar.exe unchanged.</summary>
    public static void EnsureRepresentable(IEnumerable<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (!IsRepresentable(argument))
                throw new TarArgumentEncodingException(argument, AnsiCodePage);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WideCharToMultiByte(
        uint codePage, uint flags, string wideChars, int wideCharCount,
        byte[]? multiByte, int multiByteCount, IntPtr defaultChar, IntPtr usedDefaultChar);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WideCharToMultiByte")]
    private static extern int WideCharToMultiByteUsedDefault(
        uint codePage, uint flags, string wideChars, int wideCharCount,
        byte[] multiByte, int multiByteCount, IntPtr defaultChar, out int usedDefaultChar);

    // CharSet.Unicode matters: without it a char[] is marshaled as one byte per char.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MultiByteToWideChar(
        uint codePage, uint flags, byte[] multiByte, int multiByteCount, char[]? wideChars, int wideCharCount);
}

/// <summary>
/// A name that tar.exe would receive altered (T-F266) — refused before tar.exe runs. An
/// <see cref="IOException"/>, so every tar call site reports it as an ordinary per-archive error.
/// </summary>
internal sealed class TarArgumentEncodingException(string argument, uint codePage) // NOSONAR: S3871 — deliberately internal, always caught and turned into an ArchiveError (same as SandboxSetupException)
    : IOException(
        $"The name '{argument}' contains characters that tar.exe cannot handle safely on this system " +
        $"(ANSI code page {codePage}). Use ZIP for this content.");
