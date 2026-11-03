namespace Archiver.App.Core;

/// <summary>
/// T-F432: the App's taskbar button and tray icon while an operation runs, for the whole run
/// (the App's progress already spans every archive of it). Yellow while a prompt waits; red while
/// the summary of a failed run is on screen; cleared when a clean run's summary shows or on a
/// cancel. Prompts arrive on Core's worker threads, so every input takes the lock.
/// </summary>
public sealed class OperationIndicator
{
    private readonly Lock _lock = new();
    private bool _running;
    private bool _finished;
    private bool _failed;
    private bool _indeterminate;
    private int _prompts;
    private int _percent;

    /// <summary>Raised, on the thread of the input, when <see cref="State"/> or <see cref="Percent"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>What the bar shows now.</summary>
    public TaskbarProgressState State
    {
        get
        {
            lock (_lock)
                return StateLocked();
        }
    }

    /// <summary>The run's percent, 0 to 100.</summary>
    public int Percent
    {
        get
        {
            lock (_lock)
                return _percent;
        }
    }

    /// <summary>An operation starts: whatever the last one left is forgotten.</summary>
    public void Start() => Update(() =>
    {
        _running = true;
        (_finished, _failed, _indeterminate, _prompts, _percent) = (false, false, false, 0, 0);
    });

    /// <summary>The run's progress; clamped to 0 to 100.</summary>
    public void Report(int percent) => Update(() => _percent = Math.Clamp(percent, 0, 100));

    /// <summary>The bar has nothing left to measure (finishing an archive).</summary>
    public void SetIndeterminate(bool indeterminate) => Update(() => _indeterminate = indeterminate);

    /// <summary>A prompt (conflict, password, a confirmation) waits for the user.</summary>
    public void PromptOpened() => Update(() => _prompts++);

    /// <summary>A prompt was answered.</summary>
    public void PromptClosed() => Update(() => _prompts = Math.Max(0, _prompts - 1));

    /// <summary>The run is over and its summary, an error or nothing (a cancel) is about to show.</summary>
    public void Finished(bool failed) => Update(() =>
    {
        _finished = true;
        _failed = failed;
    });

    /// <summary>Nothing of the operation is on screen any more.</summary>
    public void End() => Update(() => _running = false);

    private TaskbarProgressState StateLocked()
    {
        if (!_running)
            return TaskbarProgressState.None;
        if (_finished)
            return _failed ? TaskbarProgressState.Error : TaskbarProgressState.None;
        if (_prompts > 0)
            return TaskbarProgressState.Paused;
        return _indeterminate ? TaskbarProgressState.Indeterminate : TaskbarProgressState.Normal;
    }

    private void Update(Action change)
    {
        bool changed;
        lock (_lock)
        {
            (TaskbarProgressState State, int Percent) before = (StateLocked(), _percent);
            change();
            changed = before != (StateLocked(), _percent);
        }
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
