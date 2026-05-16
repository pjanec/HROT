using System;

namespace Hrot.SimHost.Utilities
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    public static class Logger
    {
        public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        public static void Debug(string message) => Write(LogLevel.Debug, message);
        public static void Info(string message) => Write(LogLevel.Info, message);
        public static void Warning(string message) => Write(LogLevel.Warning, message);
        public static void Error(string message) => Write(LogLevel.Error, message);

        private static void Write(LogLevel level, string message)
        {
            if (level < MinimumLevel) return;

            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            var tag = level switch
            {
                LogLevel.Debug => "DEBUG",
                LogLevel.Info => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                _ => "INFO ",
            };

            var line = $"[{ts}] [{tag}] {message}";
            if (level == LogLevel.Error)
                Console.Error.WriteLine(line);
            else
                Console.Out.WriteLine(line);
        }
    }
}
