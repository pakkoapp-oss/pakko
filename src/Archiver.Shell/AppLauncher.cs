using System.Runtime.InteropServices;
using Archiver.Core.Services;

namespace Archiver.Shell;

/// <summary>Outcome of <see cref="AppLauncher.Launch"/>.</summary>
public enum AppLaunchResult
{
    /// <summary>Archiver.App was activated.</summary>
    Launched,

    /// <summary>The selection does not fit in <see cref="LaunchArguments.MaxLength"/>; nothing was launched.</summary>
    TooManyFiles,

    /// <summary>This process has no package identity, so the App's AUMID is unknown; nothing was launched.</summary>
    NoPackage,

    /// <summary><c>ActivateApplication</c> returned a failure HRESULT.</summary>
    Failed,
}

/// <summary>
/// Opens Archiver.App on an Explorer selection (T-F232) through
/// <c>IApplicationActivationManager::ActivateApplication</c>, passing <see cref="LaunchArguments"/> —
/// replaces the <c>pakko://</c> URI scheme, which any web page or document link could launch.
/// </summary>
public static class AppLauncher
{
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    /// <summary>
    /// Builds the argument string, or returns false when it is longer than
    /// <see cref="LaunchArguments.MaxLength"/> — past the command-line limit ActivateApplication blocks
    /// forever instead of failing, so an oversized selection must never reach it.
    /// </summary>
    public static bool TryBuildArguments(LaunchOperation operation, IReadOnlyList<string> files, out string arguments)
    {
        arguments = LaunchArguments.Format(operation, files);
        return arguments.Length <= LaunchArguments.MaxLength;
    }

    /// <summary>Activates the App of this process's own package with <paramref name="files"/>.</summary>
    public static AppLaunchResult Launch(LaunchOperation operation, IReadOnlyList<string> files)
    {
        if (!TryBuildArguments(operation, files, out var arguments))
            return AppLaunchResult.TooManyFiles;

        var familyName = OwnPackageFamilyName();
        if (familyName is null)
            return AppLaunchResult.NoPackage;

        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        try
        {
            var hr = manager.ActivateApplication(familyName + "!App", arguments, ActivateOptions.None, out _);
            return hr >= 0 ? AppLaunchResult.Launched : AppLaunchResult.Failed;
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
        }
    }

    private static string? OwnPackageFamilyName()
    {
        uint length = 0;
        var rc = NativeMethods.GetCurrentPackageFamilyName(ref length, null);
        if (rc == AppModelErrorNoPackage || rc != ErrorInsufficientBuffer)
            return null;

        var buffer = new char[length];
        rc = NativeMethods.GetCurrentPackageFamilyName(ref length, buffer);
        return rc == 0 ? new string(buffer, 0, (int)length - 1) : null;
    }

    private enum ActivateOptions
    {
        None = 0,
    }

    // Declared from ShObjIdl_core.h (SDK 10.0.26100): every method returns HRESULT, so
    // [PreserveSig] keeps the HRESULT visible instead of the marshaller throwing. Only the first
    // vtable slot is called; the two after it are declared so the layout matches the header.
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, ActivateOptions options, out uint processId);

        [PreserveSig]
        int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);

        [PreserveSig]
        int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray, out uint processId);
    }

    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager // NOSONAR: S3260 — a [ComImport] coclass stays unsealed: the cast to its interface is a runtime QueryInterface, which C# rejects at compile time for a sealed class (CS0030)
    {
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, char[]? packageFamilyName);
    }
}
