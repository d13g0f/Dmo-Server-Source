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

        public static string BaseLogPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "logs");

        // 🔑 Buffers por carpeta
        private static readonly Dictionary<string, List<string>> LogBuffers = new();
        private const int BufferFlushThreshold = 100;
        private static readonly object BufferLock = new();
        private static readonly Timer FlushTimer;

        static GameLogger()
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                FlushAllBuffers().GetAwaiter().GetResult();
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
            string logEntry = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";

            lock (BufferLock)
            {
                if (!LogBuffers.ContainsKey(folder))
                    LogBuffers[folder] = new List<string>();

                LogBuffers[folder].Add(logEntry);

                int totalCount = 0;
                foreach (var buffer in LogBuffers.Values)
                    totalCount += buffer.Count;

                if (totalCount >= BufferFlushThreshold)
                    _ = FlushAllBuffers();
            }
        }

        private static async Task FlushAllBuffers()
        {
            if (!IsLoggingEnabled) return;

            Dictionary<string, List<string>> buffersToWrite;
            lock (BufferLock)
            {
                if (LogBuffers.Count == 0) return;

                buffersToWrite = new Dictionary<string, List<string>>(LogBuffers);
                LogBuffers.Clear();
            }

            foreach (var kvp in buffersToWrite)
            {
                string folder = kvp.Key;
                List<string> lines = kvp.Value;

                string logDir = Path.Combine(BaseLogPath, folder);
                Directory.CreateDirectory(logDir);
                string filePath = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd}.txt");

                try
                {
                    await File.AppendAllLinesAsync(filePath, lines);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[GameLogger] Flush failed for '{folder}': {ex.Message}");
                }
            }
        }

        private static void OnFlushTimerElapsed(object sender, ElapsedEventArgs args)
        {
            _ = FlushAllBuffers();
        }
    }
}
