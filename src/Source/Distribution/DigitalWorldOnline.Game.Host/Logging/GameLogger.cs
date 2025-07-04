using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace GameServer.Logging
{
    /// <summary>
    /// Static logger for game-related events with dynamic folder support and buffer-based writing.
    /// </summary>
public static class GameLogger
{
    public static bool IsLoggingEnabled { get; set; } = true;

    // 💡 Directorio raíz configurable
    public static string BaseLogPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "logs");

    private static readonly List<string> LogBuffer = new();
    private const int BufferFlushThreshold = 100;
    private static readonly object BufferLock = new();
    private static readonly Timer FlushTimer;

    static GameLogger()
    {
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            string finalPath = Path.Combine(BaseLogPath, "general", $"{DateTime.Now:yyyy-MM-dd}.txt");
            FlushBuffer(finalPath).GetAwaiter().GetResult();
        };

        FlushTimer = new Timer(30000);
        FlushTimer.Elapsed += OnFlushTimerElapsed;
        FlushTimer.AutoReset = true;
        FlushTimer.Start();
    }

    public static async Task LogInfo(string message, string folder = "general") =>
        await WriteLog("INFO", message, folder);

    public static async Task LogWarning(string message, string folder = "general") =>
        await WriteLog("WARNING", message, folder);

    public static async Task LogError(string message, string folder = "general") =>
        await WriteLog("ERROR", message, folder);

    private static async Task WriteLog(string level, string message, string folder)
    {
        if (!IsLoggingEnabled) return;

        folder = folder.Replace('/', '_').Replace('\\', '_').Trim();
        string logDir = Path.Combine(BaseLogPath, folder);
        Directory.CreateDirectory(logDir);

        string filePath = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd}.txt");
        string logEntry = $"[{DateTime.Now:HH:mm:ss}] [{level}] [Folder:{folder}] {message}";

        lock (BufferLock)
        {
            LogBuffer.Add(logEntry);
            if (LogBuffer.Count >= BufferFlushThreshold)
                _ = FlushBuffer(filePath);
        }
    }

    private static async Task FlushBuffer(string filePath)
    {
        if (!IsLoggingEnabled) return;

        List<string>? linesToWrite;
        lock (BufferLock)
        {
            if (LogBuffer.Count == 0) return;
            linesToWrite = new List<string>(LogBuffer);
            LogBuffer.Clear();
        }

        try
        {
            await File.AppendAllLinesAsync(filePath, linesToWrite);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GameLogger] Flush failed: {ex.Message}");
        }
    }

    private static void OnFlushTimerElapsed(object sender, ElapsedEventArgs args)
    {
        string filePath = Path.Combine(BaseLogPath, "general", $"{DateTime.Now:yyyy-MM-dd}.txt");
        _ = FlushBuffer(filePath);
    }
}

}
