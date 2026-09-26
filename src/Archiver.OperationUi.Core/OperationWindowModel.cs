using System.Globalization;
using Archiver.OperationUi.Protocol;

namespace Archiver.OperationUi.Core;

/// <summary>Where the operation window is in its life.</summary>
public enum WindowPhase
{
    /// <summary>Started, no <see cref="Begin"/> yet.</summary>
    Starting,

    /// <summary>The operation runs; the window may still be hidden.</summary>
    Running,

    /// <summary>A result is shown and waits for Close.</summary>
    Result,

    Closed,
}

/// <summary>What the window host does after an input.</summary>
public enum WindowCommand
{
    None,

    /// <summary>Re-render, still in the same visibility.</summary>
    Refresh,

    /// <summary>Make the window visible (first time) and render.</summary>
    Show,

    /// <summary>Close the window and exit.</summary>
    Close,
}

/// <summary>
/// The host's next step and the messages to send Shell first. Whenever the command is
/// <see cref="WindowCommand.Close"/> and Shell is still there, the messages end with
/// <see cref="WindowClosed"/>: EOF without it means a crash to Shell (T-F268).
/// </summary>
public sealed record WindowUpdate(WindowCommand Command, IReadOnlyList<ProtocolMessage> Send)
{
    public static readonly WindowUpdate Nothing = new(WindowCommand.None, []);
}

/// <summary>
/// The operation window's logic without WinUI (T-F268 step 4): Archiver.OperationUi only renders
/// this state and executes the returned <see cref="WindowUpdate"/>.
/// <para>
/// Nothing shows for a fast clean operation: the window stays hidden until
/// <see cref="ShowDelay"/> has passed since <see cref="Begin"/> (the host's timer calls
/// <see cref="ShowDelayElapsed"/>), or a result arrives.
/// </para>
/// </summary>
public sealed class OperationWindowModel
{
    /// <summary>Gate 0: shown 1 s after the operation starts, a clean operation under that shows nothing.</summary>
    public static readonly TimeSpan ShowDelay = TimeSpan.FromSeconds(1);

    private IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();
    private int _itemCount;

    public WindowPhase Phase { get; private set; } = WindowPhase.Starting;

    public bool IsVisible { get; private set; }

    public string Culture { get; private set; } = "";

    public bool RightToLeft { get; private set; }

    public string Title { get; private set; } = "";

    public ProgressKind Kind { get; private set; }

    /// <summary>"2 of 5 · name.zip"; null for a single archive.</summary>
    public string? ItemLine { get; private set; }

    public int Percent { get; private set; }

    public string? CurrentFile { get; private set; }

    public string? Status { get; private set; }

    public ResultText? Result { get; private set; }

    public string CancelLabel => Text(_itemCount > 1 ? WindowStrings.CancelAll : WindowStrings.Cancel);

    public string CloseLabel => Text(WindowStrings.Close);

    /// <summary>A message from Shell.</summary>
    public WindowUpdate Receive(ProtocolMessage message)
    {
        if (Phase == WindowPhase.Closed)
            return WindowUpdate.Nothing;

        switch (message)
        {
            case Hello hello:
                _strings = hello.Strings;
                Culture = hello.Culture;
                RightToLeft = hello.RightToLeft;
                return WindowUpdate.Nothing;

            case Begin begin when Phase == WindowPhase.Starting:
                Title = begin.Title;
                Kind = begin.Kind;
                Phase = WindowPhase.Running;
                return Rendered();

            case Item item when Phase == WindowPhase.Running:
                _itemCount = item.Count;
                ItemLine = item.Count > 1
                    ? string.Format(CultureInfo.CurrentCulture, Text(WindowStrings.ItemOfCount), item.Index, item.Count, item.Name)
                    : null;
                Percent = 0;
                CurrentFile = null;
                Status = null;
                return Rendered();

            case Progress progress when Phase == WindowPhase.Running:
                Percent = Math.Clamp(progress.Percent, 0, 100);
                CurrentFile = progress.CurrentFile;
                Status = progress.Status;
                return Rendered();

            case Complete { Result: null }:
                return CloseNow([]);

            case Complete complete:
                Result = complete.Result;
                Phase = WindowPhase.Result;
                return ShowOrRefresh();

            default:
                return WindowUpdate.Nothing;
        }
    }

    /// <summary>The host's one-shot timer, started when <see cref="Begin"/> arrived.</summary>
    public WindowUpdate ShowDelayElapsed() =>
        Phase == WindowPhase.Running && !IsVisible ? ShowOrRefresh() : WindowUpdate.Nothing;

    /// <summary>
    /// Cancel, Close, Esc or the title bar's X. While the operation runs this cancels it: the whole
    /// Explorer command stops (T-F269), then the window closes.
    /// </summary>
    public WindowUpdate UserClosed() => Phase switch
    {
        WindowPhase.Closed => WindowUpdate.Nothing,
        WindowPhase.Running => CloseNow([new CancelRequested()]),
        _ => CloseNow([]),
    };

    /// <summary>
    /// Shell's end of the pipe closed. A result already on screen stays until the user closes it;
    /// anything else has nobody left to report to.
    /// </summary>
    public WindowUpdate ShellDisconnected()
    {
        if (Phase is WindowPhase.Result or WindowPhase.Closed)
            return WindowUpdate.Nothing;
        Phase = WindowPhase.Closed;
        return new WindowUpdate(WindowCommand.Close, []);
    }

    private WindowUpdate Rendered() =>
        IsVisible ? new WindowUpdate(WindowCommand.Refresh, []) : WindowUpdate.Nothing;

    private WindowUpdate ShowOrRefresh()
    {
        if (IsVisible)
            return new WindowUpdate(WindowCommand.Refresh, []);
        IsVisible = true;
        return new WindowUpdate(WindowCommand.Show, []);
    }

    private WindowUpdate CloseNow(ProtocolMessage[] first)
    {
        Phase = WindowPhase.Closed;
        return new WindowUpdate(WindowCommand.Close, [.. first, new WindowClosed()]);
    }

    private string Text(string key) => _strings.TryGetValue(key, out string? value) ? value : key;
}
