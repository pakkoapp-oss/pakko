using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

namespace Archiver.ProgressWindow.ViewModels;

/// <summary>
/// ViewModel for the progress window. Connects to Archiver.Shell as a named pipe client,
/// reads progress/completion messages, and writes cancel signals back.
///
/// Named pipe protocol (newline-delimited UTF-8 JSON):
///
///   Shell → ProgressWindow:
///     {"type":"progress","percent":N,"bytesTransferred":N,"totalBytes":N}
///     {"type":"complete","success":true}
///     {"type":"complete","success":false,"errorSummary":"N error(s)"}
///     {"type":"cancelled"}
///
///   ProgressWindow → Shell:
///     {"type":"cancel"}
/// </summary>
public sealed partial class ProgressViewModel : ObservableObject
{
    // Lifecycle callbacks wired by ProgressWindow.xaml.cs.
    public Action? CloseWindow { get; set; }
    public Func<string, Task>? ShowErrorDialog { get; set; }

    [ObservableProperty] private int _percent;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private string _statusText = "Connecting...";
    [ObservableProperty] private bool _isCancelEnabled = true;

    private readonly string _pipeName;
    private readonly DispatcherQueue _dispatcherQueue;
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly object _writeLock = new();
    private bool _cancelRequested;

    public ProgressViewModel(string pipeName)
    {
        _pipeName = pipeName;
        // Capture the UI thread's DispatcherQueue while we are on it.
        // Constructor is called from ProgressWindow.xaml.cs (UI thread).
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>
    /// Connects to the Shell's named pipe server and starts the read loop.
    /// Called fire-and-forget from ProgressWindow.xaml.cs.
    /// </summary>
    public async Task InitAsync()
    {
        if (string.IsNullOrEmpty(_pipeName))
        {
            Dispatch(() => StatusText = "No pipe name provided.");
            return;
        }

        try
        {
            _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);

            // Shell creates the pipe server before launching this process.
            await _pipe.ConnectAsync(10_000).ConfigureAwait(false);

            _writer = new StreamWriter(_pipe, Encoding.UTF8, bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true
            };

            var reader = new StreamReader(_pipe, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

            await RunReadLoopAsync(reader).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Dispatch(() => StatusText = "Connection timed out.");
        }
        catch (IOException ex)
        {
            Dispatch(() => StatusText = $"Pipe error: {ex.Message}");
        }
    }

    private async Task RunReadLoopAsync(StreamReader reader)
    {
        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                break; // pipe closed by Shell — normal exit
            }

            if (line is null) break; // EOF

            try { HandleMessage(line); }
            catch { /* malformed JSON — ignore and continue */ }
        }
    }

    private void HandleMessage(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is null) return;

        string? type = node["type"]?.GetValue<string>();

        switch (type)
        {
            case "progress":
            {
                int percent = node["percent"]?.GetValue<int>() ?? 0;
                long bytesTransferred = node["bytesTransferred"]?.GetValue<long>() ?? 0;
                long totalBytes = node["totalBytes"]?.GetValue<long>() ?? 0;

                Dispatch(() =>
                {
                    Percent = percent;
                    IsIndeterminate = percent >= 100;
                    StatusText = FormatStatus(percent, bytesTransferred, totalBytes);
                });
                break;
            }

            case "complete":
            {
                bool success = node["success"]?.GetValue<bool>() ?? false;
                string? errorSummary = node["errorSummary"]?.GetValue<string>();

                Dispatch(() =>
                {
                    IsCancelEnabled = false;
                    if (success)
                    {
                        Percent = 100;
                        IsIndeterminate = false;
                        StatusText = "Done.";
                        _ = Task.Delay(1500)
                            .ContinueWith(_ => Dispatch(() => CloseWindow?.Invoke()),
                                TaskScheduler.Default);
                    }
                    else
                    {
                        StatusText = errorSummary ?? "Operation failed.";
                        _ = ShowErrorAndCloseAsync(errorSummary ?? "The operation completed with errors.");
                    }
                });
                break;
            }

            case "cancelled":
            {
                Dispatch(() =>
                {
                    IsCancelEnabled = false;
                    StatusText = "Cancelled.";
                    _ = Task.Delay(500)
                        .ContinueWith(_ => Dispatch(() => CloseWindow?.Invoke()),
                            TaskScheduler.Default);
                });
                break;
            }
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_cancelRequested) return;
        _cancelRequested = true;
        IsCancelEnabled = false;
        StatusText = "Cancelling...";

        _ = Task.Run(() =>
        {
            lock (_writeLock)
            {
                try { _writer?.WriteLine("{\"type\":\"cancel\"}"); }
                catch { /* pipe may already be closed */ }
            }
        });
    }

    private async Task ShowErrorAndCloseAsync(string message)
    {
        var tcs = new TaskCompletionSource();
        Dispatch(async () =>
        {
            try
            {
                if (ShowErrorDialog is not null)
                    await ShowErrorDialog(message);
            }
            finally
            {
                CloseWindow?.Invoke();
                tcs.TrySetResult();
            }
        });
        await tcs.Task.ConfigureAwait(false);
    }

    private void Dispatch(Action action) => _dispatcherQueue.TryEnqueue(() => action());

    private static string FormatStatus(int percent, long bytesTransferred, long totalBytes)
    {
        if (totalBytes <= 0) return $"{percent}%";
        return $"{percent}%  ·  {FormatBytes(bytesTransferred)} / {FormatBytes(totalBytes)}";
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F0} KB",
        _                => $"{bytes} B"
    };
}
