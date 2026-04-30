using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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

    // ── Syntax-highlighting token types ─────────────────────────────────────
    public enum ChunkType { Text, Number, Punctuation }

    /// <summary>A pre-tokenized span of a log message for zero-allocation rendering.</summary>
    public readonly record struct LogChunk(string Text, ChunkType Type);

    /// <summary>
    /// Tokenizes a log message string into <see cref="LogChunk"/> segments once
    /// at ingestion time so the render path never needs to allocate strings.
    /// </summary>
    public static class LogSyntaxHighlighter
    {
        public static IReadOnlyList<LogChunk> Parse(string message)
        {
            if (string.IsNullOrEmpty(message))
                return Array.Empty<LogChunk>();

            var chunks = new List<LogChunk>(8);
            int start = 0;
            ChunkType currentType = ClassifyChar(message[0]);

            for (int i = 1; i < message.Length; i++)
            {
                ChunkType t = ClassifyChar(message[i]);
                if (t != currentType)
                {
                    chunks.Add(new LogChunk(message.Substring(start, i - start), currentType));
                    start = i;
                    currentType = t;
                }
            }
            chunks.Add(new LogChunk(message.Substring(start), currentType));
            return chunks;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ChunkType ClassifyChar(char c)
        {
            if (c >= '0' && c <= '9') return ChunkType.Number;
            if (char.IsPunctuation(c) || char.IsSymbol(c)) return ChunkType.Punctuation;
            return ChunkType.Text;
        }
    }

    // ── Immutable log record ─────────────────────────────────────────────────
    /// <summary>
    /// A single message captured by an <see cref="IMessageLogSource"/>.
    /// <para><c>Chunks</c> holds pre-tokenized message segments computed at
    /// creation time so the UI render loop allocates nothing per frame.</para>
    /// <para><paramref name="FilePath"/> and <paramref name="LineNumber"/> are
    /// optional; populated by sources that parse compiler output to enable
    /// double-click navigation to the file.</para>
    /// </summary>
    public sealed record MessageLogEntry(
        DateTime                Timestamp,
        LogSeverity             Severity,
        string                  LoggerName,
        string                  Message,
        IReadOnlyList<LogChunk> Chunks,
        string?                 FilePath   = null,
        int                     LineNumber = 0);

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
