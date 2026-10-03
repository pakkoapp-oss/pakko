using Archiver.Core.IO;

namespace Archiver.Core.Tests.Helpers;

/// <summary>Names a temporary entry as a killed Pakko run on this machine would have left it.</summary>
internal static class DeadOwner
{
    /// <summary>This machine and this process id with another start time: a reused id, so the owner is gone.</summary>
    public static string Tag => $"{TempOwner.CurrentTag.Split('-')[0]}-{Environment.ProcessId}-1";

    public const string Unique = "0123456789abcdef0123456789abcdef";

    public static string Name(string prefix, string suffix = "") => $"{prefix}{Tag}-{Unique}{suffix}";
}
