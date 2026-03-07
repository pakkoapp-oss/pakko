using System;
using System.IO;

namespace Archiver.App.Services;

public sealed class LogService : ILogService
{
    private const long MaxFileSizeBytes = 1 * 1024 * 1024; // 1 MB
    private const int MaxRotatedFiles = 3;

    private readonly string _logPath;
    private readonly object _lock = new();

    public LogService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pakko", "logs");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "pakko.log");
    }

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);

    public void Error(string message, Exception? ex = null)
    {
        var text = ex is null ? message : $"{message} — {ex.GetType().Name}: {ex.Message}";
        Write("ERROR", text);
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
        lock (_lock)
        {
            RotateIfNeeded();
            File.AppendAllText(_logPath, line);
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_logPath))
            return;

        var info = new FileInfo(_logPath);
        if (info.Length < MaxFileSizeBytes)
            return;

        // Shift existing rotated files: .log.3 deleted, .log.2 → .log.3, etc.
        for (int i = MaxRotatedFiles; i >= 1; i--)
        {
            var older = $"{_logPath}.{i}";
            var newer = $"{_logPath}.{i - 1}";
            if (i == 1) newer = _logPath;

            if (File.Exists(older))
                File.Delete(older);
            if (File.Exists(newer))
                File.Move(newer, older);
        }
    }
}
