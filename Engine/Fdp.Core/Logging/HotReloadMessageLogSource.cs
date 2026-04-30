using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Fdp.Core.Logging
{
    /// <summary>
    /// <see cref="IMessageLogSource"/> that captures messages from the
    /// <c>FbtAssemblyHotReloader</c> hot-reload pipeline.
    ///
    /// <para>Wire the public helpers to the hot-reloader events after construction:</para>
    /// <code>
    /// var src = new HotReloadMessageLogSource();
    /// _hotReloader.OnReloadCompleted += src.OnReloadCompleted;
    /// _hotReloader.OnReloadFailed    += src.OnReloadFailed;
    /// </code>
    ///
    /// <para>For compiler / build-system line output, call <see cref="PushLine"/>
    /// with each raw output line.  MSBuild-style <c>file(line,col): error/warning</c>
    /// lines are parsed to extract the file path and line number for double-click
    /// navigation in the UI.</para>
    ///
    /// <para>All methods are intended to be called from the main thread
    /// (via <c>FbtAssemblyHotReloader.DrainPendingCallbacks</c>).</para>
    /// </summary>
    public sealed class HotReloadMessageLogSource : IMessageLogSource
    {
        // MSBuild / Roslyn compiler diagnostic line:
        //   path\file.cs(10,5): error CS0001: description
        //   path\file.cs(10,5): warning CS0219: description
        private static readonly Regex s_compilerRx = new(
            @"^(.*?)\((\d+),\d+\):\s*(error|warning)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly List<MessageLogEntry> _messages = new();

        // ── IMessageLogSource ────────────────────────────────────────────────
        public string SourceId    => "fbt_hotreload";
        public string DisplayName => "Hot Reload";

        public event Action<MessageLogEntry>? OnMessageAdded;

        // ── Event handlers for FbtAssemblyHotReloader ────────────────────────

        /// <summary>
        /// Subscribe to <c>FbtAssemblyHotReloader.OnReloadCompleted</c> to push
        /// an Info-level entry when a DLL reload succeeds.
        /// </summary>
        public void OnReloadCompleted(string treeName)
        {
            Push(LogSeverity.Info, $"Hot-reload completed: {treeName}");
        }

        /// <summary>
        /// Subscribe to <c>FbtAssemblyHotReloader.OnReloadFailed</c> to push
        /// an Error-level entry when a DLL reload fails.
        /// </summary>
        public void OnReloadFailed(string dllPath, Exception ex)
        {
            Push(LogSeverity.Error,
                $"Hot-reload FAILED: {Path.GetFileName(dllPath)}: {ex.Message}");
        }

        /// <summary>
        /// Pushes a raw output line (e.g. a build system log line).
        /// MSBuild-style compiler diagnostics are parsed to extract file/line for navigation.
        /// </summary>
        public void PushLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            var m = s_compilerRx.Match(line);
            if (m.Success)
            {
                string? filePath = m.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(filePath)) filePath = null;
                int.TryParse(m.Groups[2].Value, out int lineNum);
                bool isError = m.Groups[3].Value.Equals("error", StringComparison.OrdinalIgnoreCase);
                var sev = isError ? LogSeverity.Error : LogSeverity.Warning;

                var entry = new MessageLogEntry(DateTime.Now, sev, "Compiler", line, filePath, lineNum);
                _messages.Add(entry);
                OnMessageAdded?.Invoke(entry);
                return;
            }

            // Heuristic severity for non-diagnostic lines
            LogSeverity heuristic;
            if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("FAILED", StringComparison.Ordinal) >= 0)
                heuristic = LogSeverity.Error;
            else if (line.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0)
                heuristic = LogSeverity.Warning;
            else if (line.IndexOf("succeeded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     line.IndexOf("completed", StringComparison.OrdinalIgnoreCase) >= 0)
                heuristic = LogSeverity.Info;
            else
                heuristic = LogSeverity.Debug;

            Push(heuristic, line);
        }

        // ── IMessageLogSource ────────────────────────────────────────────────
        public IReadOnlyList<MessageLogEntry> GetMessages() => _messages;

        public void Clear() => _messages.Clear();

        // ── Private ──────────────────────────────────────────────────────────
        private void Push(LogSeverity severity, string message)
        {
            var entry = new MessageLogEntry(DateTime.Now, severity, "HotReload", message);
            _messages.Add(entry);
            OnMessageAdded?.Invoke(entry);
        }
    }
}
