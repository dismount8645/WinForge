using System.Diagnostics;
using System.Threading;

namespace WingetStore.Services;

public static class LogService
{
    private static readonly string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WingetStore", "logs");
    private static readonly string LogFile = Path.Combine(LogDir, "app.log");
    private static readonly Lock LockObj = new();
    [DebuggerNonUserCode] static LogService() { try { Directory.CreateDirectory(LogDir); } catch (Exception ex) { Debug.WriteLine($"Failed to create log directory: {ex.Message}"); } }
    public static void LogInfo(string message) => WriteLog("INFO", message);
    public static void LogError(string message, Exception? ex = null) => WriteLog("ERROR", ex != null ? $"{message} | Exception: {ex.Message}\nStack: {ex.StackTrace}" : message);
    internal static string FormatLogEntry(string level, string message, DateTime timestamp)
    {
        return $"[{timestamp:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
    }
    private static void WriteLog(string level, string message) { string line = FormatLogEntry(level, message, DateTime.Now); Debug.WriteLine(line); try { lock (LockObj) File.AppendAllText(LogFile, line + Environment.NewLine); } catch (Exception ex) { Debug.WriteLine($"Log write failed: {ex.Message}"); } }
}
