using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Archiver.Shell;

/// <summary>Options for <see cref="IApplicationActivationManager.ActivateApplication"/>.</summary>
internal enum ActivateOptions
{
    None = 0,
}

// Declared from ShObjIdl_core.h (SDK 10.0.26100): every method returns HRESULT, so [PreserveSig]
// keeps the HRESULT visible instead of the marshaller throwing. Only the first vtable slot is
// called; the two after it are declared so the layout matches the header. T-F288: source-generated,
// created through ShellCom.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
internal partial interface IApplicationActivationManager
{
    [PreserveSig]
    int ActivateApplication(string appUserModelId, string arguments, ActivateOptions options, out uint processId);

    [PreserveSig]
    int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);

    [PreserveSig]
    int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
}
