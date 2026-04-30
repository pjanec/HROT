using System;
using System.Collections.Generic;
using NLog;
using NLog.Targets;

namespace Fdp.Core.Logging
{
    /// <summary>
    /// NLog <see cref="Target"/> that captures every log event into an in-memory list
    /// and exposes it as an <see cref="IMessageLogSource"/> for the <c>MessageLogWindow</c>.
    ///
    /// <para>A process-wide <see cref="SharedInstance"/> is provided so that
    /// <c>Program.cs</c> can register it with the NLog config once at startup,
    /// while subsystems later attach it to their <see cref="MessageLogRegistry"/>.</para>
    ///
    /// <para>Thread safety: <see cref="Write"/> is called by NLog from a background
    /// thread; the internal list is protected by a lock.</para>
    /// </summary>
    [Target("UiMessageLog")]
    public sealed class NLogMessageLogTarget : Target, IMessageLogSource
    {
        // ── Shared singleton ─────────────────────────────────────────────────
        private static readonly NLogMessageLogTarget s_shared = new();

        /// <summary>
        /// Process-wide singleton.  Register this with <c>NLog.LogManager.Configuration</c>
        /// at startup, then add it to a <see cref="MessageLogRegistry"/> for the UI.
        /// </summary>
        public static NLogMessageLogTarget SharedInstance => s_shared;

        // ── IMessageLogSource ────────────────────────────────────────────────
        public string SourceId     => "global_nlog";
        public string DisplayName  => "NLog (Global)";

        public event Action<MessageLogEntry>? OnMessageAdded;

        // ── Storage ──────────────────────────────────────────────────────────
        private readonly List<MessageLogEntry> _messages = new();
        private readonly object _lock = new();

        // ── NLog override ────────────────────────────────────────────────────
        protected override void Write(LogEventInfo logEvent)
        {
            string msg = logEvent.FormattedMessage ?? string.Empty;
            if (logEvent.Exception != null)
                msg = msg + Environment.NewLine + logEvent.Exception.ToString();

            var entry = new MessageLogEntry(
                logEvent.TimeStamp,
                MapSeverity(logEvent.Level),
                logEvent.LoggerName ?? "Unknown",
                msg);

            lock (_lock)
                _messages.Add(entry);

            // Fire on the NLog thread; subscribers must tolerate cross-thread calls.
            OnMessageAdded?.Invoke(entry);
        }

        // ── IMessageLogSource ────────────────────────────────────────────────
        public IReadOnlyList<MessageLogEntry> GetMessages()
        {
            lock (_lock)
                return _messages.ToArray();
        }

        public void Clear()
        {
            lock (_lock)
                _messages.Clear();
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private static LogSeverity MapSeverity(LogLevel level)
        {
            if (level == LogLevel.Trace) return LogSeverity.Trace;
            if (level == LogLevel.Debug) return LogSeverity.Debug;
            if (level == LogLevel.Info)  return LogSeverity.Info;
            if (level == LogLevel.Warn)  return LogSeverity.Warning;
            if (level == LogLevel.Error) return LogSeverity.Error;
            if (level == LogLevel.Fatal) return LogSeverity.Critical;
            return LogSeverity.Info;
        }
    }
}
