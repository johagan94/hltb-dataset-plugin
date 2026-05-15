using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace HltbDatasetPlugin;

/// <summary>
/// Simple file-based logger. Thread-safe via lock.
/// Logs to hltb_plugin.log in the plugin directory.
/// </summary>
public static class HltbLogger
{
    private static readonly object _lock = new();
    private static string? _logPath;
    private static bool _enabled = true;
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5MB rotation threshold

    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public static string LogPath
    {
        get
        {
            if (_logPath != null) return _logPath;
            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
                _logPath = Path.Combine(dir, "hltb_plugin.log");
            }
            catch
            {
                _logPath = Path.Combine(Path.GetTempPath(), "hltb_plugin.log");
            }
            return _logPath;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Debug(string message) => Write("DEBUG", message);

    public static void Error(string message, Exception ex)
    {
        Write("ERROR", $"{message} | {ex.GetType().Name}: {ex.Message}");
        if (ex.StackTrace != null)
            Write("ERROR", $"  Stack: {ex.StackTrace}");
    }

    private static void Write(string level, string message)
    {
        if (!_enabled) return;

        try
        {
            lock (_lock)
            {
                RotateIfNeeded();
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level,-5}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch { /* never throw from logger */ }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var fi = new FileInfo(LogPath);
            if (fi.Exists && fi.Length > MaxLogSizeBytes)
            {
                var bakPath = LogPath + ".old";
                if (File.Exists(bakPath)) File.Delete(bakPath);
                File.Move(LogPath, bakPath);
            }
        }
        catch { }
    }

    /// <summary>Log a section header for visual separation.</summary>
    public static void Section(string title)
    {
        Write("INFO", $"===== {title} =====");
    }
}
