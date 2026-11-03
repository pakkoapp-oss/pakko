using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

// T-F432: compiled into Archiver.OperationUi and, linked, into Archiver.App - one copy of the COM
// declaration for both windows' taskbar buttons.
namespace Archiver.Taskbar;

/// <summary>
/// T-F432: the progress bar under one window's taskbar button (<c>ITaskbarList3</c>). Shows the last
/// state asked for again when Windows creates the button anew (<c>TaskbarButtonCreated</c>: the
/// window first shows, it comes back from the tray, Explorer restarts), since a new button starts
/// empty. Created, called and disposed on the window's UI thread (the object is apartment-threaded).
/// </summary>
internal sealed unsafe partial class TaskbarButton : IDisposable
{
    // shobjidl_core.idl: coclass TaskbarList.
    private static readonly Guid TaskbarListClass = new("56FDF344-FD6D-11D0-958A-006097C9A090");
    private const uint ClassContextInprocServer = 0x1;
    private const uint StateNoProgress = 0x0;
    private const uint StateIndeterminate = 0x1;
    private const nuint SubclassId = 0x7AB5;
    private static readonly StrategyBasedComWrappers Wrappers = new();
    private static readonly uint TaskbarButtonCreated = RegisterWindowMessageW("TaskbarButtonCreated");

    private readonly IntPtr _hwnd;
    private GCHandle _self;
    private ITaskbarList3? _list;
    private bool _unavailable;
    private bool _disposed;
    private (uint State, int Percent) _wanted = (StateNoProgress, 0);
    private (uint State, int Percent)? _shown;

    public TaskbarButton(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _self = GCHandle.Alloc(this);
        // Without the subclass only a later change shows again what a recreated button lost.
        if (!SetWindowSubclass(hwnd, &SubclassProc, SubclassId, (nuint)GCHandle.ToIntPtr(_self)))
            _self.Free();
    }

    /// <summary>
    /// Shows <paramref name="state"/> (a <c>TBPFLAG</c>) at <paramref name="percent"/>; a repeat of
    /// what is already shown costs nothing.
    /// </summary>
    public void Show(uint state, int percent)
    {
        _wanted = (state, Math.Clamp(percent, 0, 100));
        if (!_disposed && _shown != _wanted)
            Apply();
    }

    public void Dispose()
    {
        _disposed = true;
        if (_self.IsAllocated)
        {
            // FALSE only for a window already destroyed, whose subclass went with it.
            _ = RemoveWindowSubclass(_hwnd, &SubclassProc, SubclassId);
            _self.Free();
        }
        if (_list is not null)
            ((ComObject)(object)_list).FinalRelease();
        _list = null;
    }

    private void Apply()
    {
        ITaskbarList3? list = List();
        if (list is null)
            return;
        // SetProgressValue turns "no progress" and "indeterminate" into "normal", so those two get
        // the state alone, and the others the value first, then their colour.
        int hr = 0;
        if (_wanted.State is not (StateNoProgress or StateIndeterminate))
            hr = list.SetProgressValue(_hwnd, (ulong)_wanted.Percent, 100);
        if (hr >= 0)
            hr = list.SetProgressState(_hwnd, _wanted.State);
        // A failure means the button does not exist yet (a hidden window): TaskbarButtonCreated
        // calls Apply again once it does, so nothing is reported here.
        _shown = hr >= 0 ? _wanted : null;
    }

    private ITaskbarList3? List()
    {
        if (_list is not null || _unavailable)
            return _list;
        Guid interfaceId = typeof(ITaskbarList3).GUID;
        int hr = CoCreateInstance(TaskbarListClass, IntPtr.Zero, ClassContextInprocServer, interfaceId, out IntPtr unknown);
        if (hr < 0)
        {
            // No taskbar (a server core, a shell replacement): the progress shows in the window only.
            _unavailable = true;
            return null;
        }
        try
        {
            var list = (ITaskbarList3)Wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.UniqueInstance);
            if (list.HrInit() < 0)
            {
                ((ComObject)(object)list).FinalRelease();
                _unavailable = true;
                return null;
            }
            _list = list;
            return list;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint refData)
    {
        if (message == TaskbarButtonCreated && GCHandle.FromIntPtr((IntPtr)refData).Target is TaskbarButton { _disposed: false } button)
        {
            button._shown = null;
            button.Apply();
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr unknown);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(IntPtr hwnd, delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, nuint, nuint, IntPtr> proc, nuint id, nuint refData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(IntPtr hwnd, delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, nuint, nuint, IntPtr> proc, nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}

/// <summary>
/// shobjidl_core.idl, in vtable order: ITaskbarList (HrInit, AddTab, DeleteTab, ActivateTab,
/// SetActiveAlt), ITaskbarList2 (MarkFullscreenWindow), then ITaskbarList3's first two methods.
/// The rest of ITaskbarList3 is not called, so it is not declared: a prefix of the vtable is enough.
/// </summary>
[GeneratedComInterface]
[Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
internal partial interface ITaskbarList3
{
    [PreserveSig] int HrInit();
    [PreserveSig] int AddTab(IntPtr hwnd);
    [PreserveSig] int DeleteTab(IntPtr hwnd);
    [PreserveSig] int ActivateTab(IntPtr hwnd);
    [PreserveSig] int SetActiveAlt(IntPtr hwnd);
    [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, int fullscreen);
    [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
    [PreserveSig] int SetProgressState(IntPtr hwnd, uint flags);
}
