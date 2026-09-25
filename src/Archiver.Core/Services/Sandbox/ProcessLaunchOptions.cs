namespace Archiver.Core.Services.Sandbox;

/// <summary>
/// How <see cref="SandboxedProcessLauncher"/> starts one process. The defaults give a plain
/// launch: no AppContainer, no Job Object, stderr returned whole at the end.
/// </summary>
/// <param name="AppContainerSid">Runs the process inside this AppContainer (no capabilities); null runs it at the caller's own identity.</param>
/// <param name="Job">Assigns the process to this Job Object before it runs.</param>
/// <param name="OnStdErrLine">Called once per non-empty stderr line as it arrives.</param>
/// <param name="OnProcessStarted">Called with the child's process ID once it has been created (tests).</param>
/// <param name="StdIn">A synchronous handle the child reads as stdin (T-F233: the archive itself, so tar.exe gets no path and no ACE on the user's file).</param>
/// <param name="WorkingDirectory">The child's current directory; relative arguments resolve against it.</param>
internal sealed record ProcessLaunchOptions(
    SafeSidHandle? AppContainerSid = null,
    SafeJobObjectHandle? Job = null,
    Action<string>? OnStdErrLine = null,
    Action<int>? OnProcessStarted = null,
    Microsoft.Win32.SafeHandles.SafeFileHandle? StdIn = null,
    string? WorkingDirectory = null);
