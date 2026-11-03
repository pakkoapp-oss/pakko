namespace Archiver.App.Core;

/// <summary>
/// T-F432: what the taskbar shows under a window's button. The values are <c>ITaskbarList3</c>'s
/// own <c>TBPFLAG</c>s, so the caller passes them to <c>SetProgressState</c> unchanged.
/// </summary>
public enum TaskbarProgressState : uint
{
    /// <summary>No bar: nothing runs, or a clean run's summary shows.</summary>
    None = 0x0,
    /// <summary>A moving bar with no percent.</summary>
    Indeterminate = 0x1,
    /// <summary>Green, at the percent.</summary>
    Normal = 0x2,
    /// <summary>Red: the run ended with errors.</summary>
    Error = 0x4,
    /// <summary>Yellow: a prompt waits for the user.</summary>
    Paused = 0x8,
}
