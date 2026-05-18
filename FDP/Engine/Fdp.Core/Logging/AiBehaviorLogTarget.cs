using System;
using System.Collections.Generic;
using NLog;
using NLog.Targets;

namespace Fdp.Core.Logging
{
    /// <summary>
    /// NLog <see cref="Target"/> that captures log events from the <c>AI.Behavior*</c>
    /// logger family and exposes them as an <see cref="IMessageLogSource"/> for the
    /// dedicated "AI Behaviors" tab in <c>MessageLogWindow</c>.
    ///
    /// <para>Configure NLog to route the AI logger to this target at startup:</para>
    /// <code>
    /// logConfig.AddRule(LogLevel.Debug, LogLevel.Fatal,
    ///     AiBehaviorLogTarget.SharedInstance, "AI.Behavior*");
    /// </code>
    ///
    /// <para>Then register the tab with the UI registry:</para>
    /// <code>
    /// windowManager.MessageLogRegistry?.RegisterSource(AiBehaviorLogTarget.SharedInstance);
    /// </code>
    ///
    /// <para>Thread safety: <see cref="Write"/> is called by NLog from a background
    /// thread; the internal list is protected by a lock.</para>
    /// </summary>
    [Target("AiBehaviorLog")]
    public sealed class AiBehaviorLogTarget : Target, IMessageLogSource
    {
        // ── Shared singleton ─────────────────────────────────────────────────
        private static readonly AiBehaviorLogTarget s_shared = new();

        /// <summary>
        /// Process-wide singleton.  Register this with <c>NLog.LogManager.Configuration</c>
        /// at startup, then add it to a <see cref="MessageLogRegistry"/> for the UI.
        /// </summary>
        public static AiBehaviorLogTarget SharedInstance => s_shared;

        // ── IMessageLogSource ────────────────────────────────────────────────
        public string SourceId    => "ai_behavior_log";
        public string DisplayName => "AI Behaviors";

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
                logEvent.LoggerName ?? "AI.Behavior",
                msg,
                LogSyntaxHighlighter.Parse(msg));

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
