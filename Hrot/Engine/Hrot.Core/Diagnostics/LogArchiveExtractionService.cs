using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hrot.Core.Diagnostics
{
    /// <summary>
    /// Default implementation of <see cref="ILogArchiveExtractionService"/>.
    /// <para>
    /// Reads log files that match the pattern <c>{SubsystemName}_{NodeId}*.log</c> from
    /// <c>LogDirectory</c>; coarsely filters them by file age; then writes qualifying lines
    /// to a single archive file.
    /// </para>
    /// <para>
    /// Each line is read into a reusable char buffer (O(1) memory per file) and the
    /// timestamp / severity tokens are extracted using <see cref="ReadOnlySpan{T}"/> slicing
    /// — no <c>string.Split</c>.
    /// </para>
    /// </summary>
    public sealed class LogArchiveExtractionService : ILogArchiveExtractionService
    {
        private readonly string _logDirectory;
        private readonly string _subsystemName;
        private readonly int    _nodeId;

        /// <param name="logDirectory">Directory that contains the log files.</param>
        /// <param name="subsystemName">Name prefix used to build the file glob pattern.</param>
        /// <param name="nodeId">Node identifier suffix used to build the file glob pattern.</param>
        public LogArchiveExtractionService(string logDirectory, string subsystemName, int nodeId)
        {
            _logDirectory  = logDirectory  ?? throw new ArgumentNullException(nameof(logDirectory));
            _subsystemName = subsystemName ?? throw new ArgumentNullException(nameof(subsystemName));
            _nodeId        = nodeId;
        }

        /// <inheritdoc/>
        public async Task<int> ExtractLogsAsync(
            string targetFilePath,
            int    severityThreshold,
            float  maxAgeHours,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_logDirectory) || !Directory.Exists(_logDirectory))
                return 0;

            if (string.IsNullOrWhiteSpace(targetFilePath))
                throw new ArgumentException("Target file path must not be empty.", nameof(targetFilePath));

            string pattern = $"{_subsystemName}_{_nodeId}*.log";
            string[] files;
            try { files = Directory.GetFiles(_logDirectory, pattern); }
            catch (Exception) { return 0; }

            var cutoff = maxAgeHours < float.MaxValue
                ? DateTime.UtcNow.AddHours(-maxAgeHours)
                : DateTime.MinValue;

            int linesWritten = 0;

            var targetDir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            await using var writer = new StreamWriter(targetFilePath, append: false);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                // Coarse file-level age filter using last-write time.
                if (maxAgeHours < float.MaxValue)
                {
                    try { if (File.GetLastWriteTimeUtc(file) < cutoff) continue; }
                    catch { continue; }
                }

                // Open with FileShare.ReadWrite so live processes can still write to the file.
                FileStream? fs = null;
                try
                {
                    fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                }
                catch { continue; }

                await using (fs)
                using (var reader = new StreamReader(fs, leaveOpen: true))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync(ct)) != null)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (!LinePassesFilter(line.AsSpan(), severityThreshold)) continue;

                        await writer.WriteLineAsync(line.AsMemory(), ct);
                        linesWritten++;
                    }
                }
            }

            return linesWritten;
        }

        /// <summary>
        /// Determines whether a log line meets the severity threshold.
        /// Supports two common formats:
        /// <list type="bullet">
        ///   <item>NLog pipe format: <c>HH:mm:ss.fff | LEVEL | ...</c></item>
        ///   <item>Bracket format: <c>[LEVEL] ...</c> or <c>[N] ...</c></item>
        /// </list>
        /// Lines that cannot be parsed pass through by default (fail-safe).
        /// </summary>
        private static bool LinePassesFilter(ReadOnlySpan<char> line, int severityThreshold)
        {
            // No filtering requested.
            if (severityThreshold <= 0) return true;
            if (line.IsEmpty) return false;

            // ── Bracket format: [LEVEL] or [N] ──────────────────────────────
            if (line[0] == '[')
            {
                int closeIdx = line.IndexOf(']');
                if (closeIdx > 1)
                {
                    var token = line.Slice(1, closeIdx - 1).Trim();

                    // Numeric severity.
                    if (int.TryParse(token, out int numericSev))
                        return numericSev >= severityThreshold;

                    // Named severity (e.g. "WARN", "ERROR").
                    return MapNamedSeverity(token) >= severityThreshold;
                }
            }

            // ── NLog pipe format: "HH:mm:ss.fff | LEVEL | ..." ──────────────
            int firstPipe = line.IndexOf('|');
            if (firstPipe >= 0 && firstPipe < line.Length - 1)
            {
                int secondPipe = line.Slice(firstPipe + 1).IndexOf('|');
                if (secondPipe >= 0)
                {
                    var levelToken = line.Slice(firstPipe + 1, secondPipe).Trim();
                    return MapNamedSeverity(levelToken) >= severityThreshold;
                }
            }

            // Unknown format — include by default.
            return true;
        }

        /// <summary>Maps a named NLog severity token to a numeric level (Trace=0 .. Fatal=5).</summary>
        private static int MapNamedSeverity(ReadOnlySpan<char> token)
        {
            // Common NLog level names (uppercase or mixed case).
            if (token.Equals("TRACE", StringComparison.OrdinalIgnoreCase)) return 0;
            if (token.Equals("DEBUG", StringComparison.OrdinalIgnoreCase)) return 1;
            if (token.Equals("INFO",  StringComparison.OrdinalIgnoreCase)) return 2;
            if (token.Equals("WARN",  StringComparison.OrdinalIgnoreCase)) return 3;
            if (token.Equals("WARNING", StringComparison.OrdinalIgnoreCase)) return 3;
            if (token.Equals("ERROR", StringComparison.OrdinalIgnoreCase)) return 4;
            if (token.Equals("FATAL", StringComparison.OrdinalIgnoreCase)) return 5;

            // Unknown level — include.
            return 0;
        }
    }
}
