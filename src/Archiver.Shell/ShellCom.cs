using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Archiver.Shell;

/// <summary>
/// T-F288: creates the in-process COM objects Shell uses through source-generated interfaces
/// ([GeneratedComInterface]) and releases them deterministically. Both classes are registered
/// ThreadingModel=Both, so Shell's MTA threads call them directly, with no proxy — as the
/// [ComImport] wrappers did; callers serialize their calls themselves (Win32OperationUi's lock).
/// </summary>
internal static partial class ShellCom
{
    private const uint ClassContextInprocServer = 0x1;

    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Creates <paramref name="classId"/> and returns its <typeparamref name="T"/>
    /// interface; throws <see cref="COMException"/> when the class cannot be created.</summary>
    public static T Create<T>(Guid classId) where T : class
    {
        Guid interfaceId = typeof(T).GUID;
        int hr = CoCreateInstance(classId, IntPtr.Zero, ClassContextInprocServer, interfaceId, out IntPtr unknown);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            // A unique instance, so Release below drops exactly the references this wrapper holds.
            return (T)Wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>Releases the native object behind a wrapper made by <see cref="Create{T}"/>.</summary>
    public static void Release(object comObject) => ((ComObject)comObject).FinalRelease();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr unknown);
}
