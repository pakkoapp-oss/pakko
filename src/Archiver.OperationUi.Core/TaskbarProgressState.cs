namespace Archiver.OperationUi.Core;

/// <summary>
/// T-F432: what the taskbar shows under the operation window's button (the App has its own copy).
/// The values are <c>ITaskbarList3</c>'s own <c>TBPFLAG</c>s, so the caller passes them to
/// <c>SetProgressState</c> unchanged.
/// </summary>
public enum TaskbarProgressState : uint
{
    None = 0x0,
    Indeterminate = 0x1,
    Normal = 0x2,
    Error = 0x4,
    Paused = 0x8,
}
