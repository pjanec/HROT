using System;

namespace Hrot.SimHost.Utilities
{
    /// <summary>
    /// Severity level for <see cref="Logger"/> messages (TASK-S5.3).
    /// </summary>
    public enum LogLevel
    {
        Debug   = 0,
        Info    = 1,
        Warning = 2,
        Error   = 3
    }

    /// <summary>
    /// Lightweight static logger that writes timestamped, level-tagged lines to
    /// <see cref="Console"/>.
    ///
    /// <para>Set <see cref="MinimumLevel"/> before the simulation loop to suppress
    /// low-priority messages (e.g. set to <see cref="LogLevel.Warning"/> in
    /// production to suppress debug/info noise).</para>
    /// </summary>
    public static class Logger
    {
        /// <summary>
        /// Messages below this level are discarded without any output.
        /// Defaults to <see cref="LogLevel.Info"/>.
        /// </summary>
        public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Write a debug-level message (suppressed by default).</summary>
        public static void Debug(string message)   => Log(LogLevel.Debug,   message);

        /// <summary>Write an informational message.</summary>
        public static void Info(string message)    => Log(LogLevel.Info,    message);

        /// <summary>Write a warning message.</summary>
        public static void Warning(string message) => Log(LogLevel.Warning, message);

        /// <summary>Write an error message.</summary>
        public static void Error(string message)   => Log(LogLevel.Error,   message);

        // ── Core ──────────────────────────────────────────────────────────────────

        private static void Log(LogLevel level, string message)
        {
            if (level < MinimumLevel) return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var levelStr  = level switch
            {
                LogLevel.Debug   => "DEBUG",
                LogLevel.Info    => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error   => "ERROR",
                _                => "     "
            };

            var line = $"[{timestamp}] [{levelStr}] {message}";

            if (level >= LogLevel.Error)
                Console.Error.WriteLine(line);
            else
                Console.WriteLine(line);
        }
    }
}
