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
/// <param name="StdInData">Bytes written to the child's stdin through a pipe, then closed (T-F283: tar.exe's "-T -" name list). Not combined with <paramref name="StdIn"/>.</param>
/// <param name="WorkingDirectory">The child's current directory; relative arguments resolve against it.</param>
/// <param name="OutputEncoding">How stdout/stderr are decoded; null reads them as UTF-8.</param>
/// <param name="OnStdOutDiscarded">Non-null: stdout is read and thrown away, never kept, and this is called with the size of each piece (T-F397: "tar -x -O" writes every file's content there); the returned stdout is then empty.</param>
/// <param name="OnStdOutBytes">Non-null: called with stdout's bytes as the child wrote them, before decoding (T-F465: the names tar.exe printed, which a name sent back to it must equal).</param>
/// <param name="StdOutLines">Non-null: stdout is read line by line, each line given to it as it arrives and never kept, within its limits; the returned stdout is then empty (T-F470: the archive helper's protocol).</param>
/// <param name="DiscardStdErr">True: stderr is read and thrown away, never kept; the returned stderr is then empty (T-F470: a child Pakko does not trust).</param>
/// <param name="StdOutLimit">Non-null: stdout is counted as it arrives; past its lines or bytes the child is killed and the run fails with the limit's exception (T-F513: one tar.exe listing line per entry).</param>
/// <param name="EnvironmentOverrides">Variables the child gets with these values, on top of this process's whole environment (T-F342: TMP/TEMP for libarchive's 7z writer); null hands the environment over unchanged.</param>
internal sealed record ProcessLaunchOptions(
    SafeSidHandle? AppContainerSid = null,
    SafeJobObjectHandle? Job = null,
    Action<string>? OnStdErrLine = null,
    Action<int>? OnProcessStarted = null,
    Microsoft.Win32.SafeHandles.SafeFileHandle? StdIn = null,
    string? WorkingDirectory = null,
    System.Text.Encoding? OutputEncoding = null,
    byte[]? StdInData = null,
    IReadOnlyDictionary<string, string>? EnvironmentOverrides = null,
    Action<int>? OnStdOutDiscarded = null,
    Action<byte[]>? OnStdOutBytes = null,
    StdOutLines? StdOutLines = null,
    bool DiscardStdErr = false,
    StdOutLimit? StdOutLimit = null);

/// <summary>Takes one stdout line, without its '\n'.</summary>
internal delegate void StdOutLineHandler(ReadOnlySpan<byte> line);

/// <summary>
/// T-F470: stdout as lines, for a child Pakko does not trust. A line longer than
/// <paramref name="MaxLineBytes"/>, more than <paramref name="MaxTotalBytes"/> in all, output that ends
/// inside a line, or an exception from <paramref name="OnLine"/> kills the child and fails the run.
/// </summary>
internal sealed record StdOutLines(StdOutLineHandler OnLine, int MaxLineBytes, long MaxTotalBytes);

/// <summary>
/// T-F513: the most '\n' bytes and bytes in all a child may write to stdout; past either the child
/// is killed and the run fails with <paramref name="Exceeded"/>'s exception.
/// </summary>
internal sealed record StdOutLimit(long MaxLines, long MaxBytes, Func<Exception> Exceeded);
