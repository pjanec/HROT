using System;
using System.Collections.Generic;

namespace Fdp.Core.Logging
{
    // ── Severity levels (ordered lowest to highest) ─────────────────────────
    public enum LogSeverity
    {
        Trace,
        Debug,
        Info,
        Warning,
        Error,
        Critical,
    }

    // ── Immutable log record ─────────────────────────────────────────────────
    /// <summary>
    /// A single message captured by an <see cref="IMessageLogSource"/>.
    /// <para><paramref name="FilePath"/> and <paramref name="LineNumber"/> are optional;
    /// they are populated by sources that parse compiler output so the UI can open
    /// the file in the default editor on double-click.</para>
    /// </summary>
    public sealed record MessageLogEntry(
        DateTime    Timestamp,
        LogSeverity Severity,
        string      LoggerName,
        string      Message,
        string?     FilePath   = null,
        int         LineNumber = 0);

    // ── Source interface ─────────────────────────────────────────────────────
    /// <summary>
    /// A named source of <see cref="MessageLogEntry"/> records displayed as a tab
    /// in the <c>MessageLogWindow</c>.
    /// </summary>
    public interface IMessageLogSource
    {
        /// <summary>Stable internal identifier (used as ImGui state key).</summary>
        string SourceId { get; }

        /// <summary>Human-readable tab label shown in the UI.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Returns a snapshot of all currently accumulated messages.
        /// Thread-safe; may allocate a copy.
        /// </summary>
        IReadOnlyList<MessageLogEntry> GetMessages();

        /// <summary>Removes all accumulated messages.</summary>
        void Clear();

        /// <summary>
        /// Fired (potentially on a non-UI thread) when a new entry is appended.
        /// The UI subscribes to this for the attention-notification badge on the tab.
        /// </summary>
        event Action<MessageLogEntry>? OnMessageAdded;
    }

    // ── Registry ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Runtime registry that collects <see cref="IMessageLogSource"/> instances.
    /// An instance is injected into <c>MessageLogWindow</c> and stored on
    /// <c>WindowManager.MessageLogRegistry</c> so subsystems can register
    /// additional sources from their <c>RegisterWindows</c> override.
    /// </summary>
    public sealed class MessageLogRegistry
    {
        private readonly List<IMessageLogSource> _sources = new();

        /// <summary>Ordered list of registered sources (one tab each).</summary>
        public IReadOnlyList<IMessageLogSource> Sources => _sources;

        /// <summary>
        /// Adds <paramref name="source"/> to the registry.
        /// No-op if the same instance was already added.
        /// </summary>
        public void RegisterSource(IMessageLogSource source)
        {
            if (!_sources.Contains(source))
                _sources.Add(source);
        }

        /// <summary>Removes <paramref name="source"/> from the registry.</summary>
        public void UnregisterSource(IMessageLogSource source) =>
            _sources.Remove(source);
    }
}
