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
public static partial class AppLauncher
{
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    /// <summary>CLSID_ApplicationActivationManager.</summary>
    internal static readonly Guid ActivationManagerClassId = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");

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
        if (!TryBuildArguments(operation, files, out string? arguments))
            return AppLaunchResult.TooManyFiles;

        string? familyName = OwnPackageFamilyName();
        if (familyName is null)
            return AppLaunchResult.NoPackage;

        IApplicationActivationManager manager = ShellCom.Create<IApplicationActivationManager>(ActivationManagerClassId);
        try
        {
            int hr = manager.ActivateApplication(familyName + "!App", arguments, ActivateOptions.None, out _);
            return hr >= 0 ? AppLaunchResult.Launched : AppLaunchResult.Failed;
        }
        finally
        {
            ShellCom.Release(manager);
        }
    }

    private static string? OwnPackageFamilyName()
    {
        uint length = 0;
        int rc = NativeMethods.GetCurrentPackageFamilyName(ref length, null);
        if (rc == AppModelErrorNoPackage || rc != ErrorInsufficientBuffer)
            return null;

        char[] buffer = new char[length];
        rc = NativeMethods.GetCurrentPackageFamilyName(ref length, buffer);
        return rc == 0 ? new string(buffer, 0, (int)length - 1) : null;
    }


    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        public static partial int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, [Out] char[]? packageFamilyName);
    }
}
