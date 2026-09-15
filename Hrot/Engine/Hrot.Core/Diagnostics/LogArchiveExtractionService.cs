using System;
using System.Collections.Generic;
using System.Globalization;
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
            if (string.IsNullOrWhiteSpace(targetFilePath))
                throw new ArgumentException("Target file path must not be empty.", nameof(targetFilePath));

            var filesToProcess = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Discover files currently targeted by NLog in this process.
            var nlogConfig = NLog.LogManager.Configuration;
            if (nlogConfig != null)
            {
                var probeEvent = new NLog.LogEventInfo { TimeStamp = DateTime.UtcNow };
                foreach (var target in nlogConfig.AllTargets)
                {
                    if (target is NLog.Targets.FileTarget fileTarget)
                    {
                        string activeFile;
                        try { activeFile = fileTarget.FileName.Render(probeEvent); }
                        catch { continue; }
                        if (string.IsNullOrWhiteSpace(activeFile)) continue;

                        var dir = Path.GetDirectoryName(activeFile);
                        var baseName = Path.GetFileNameWithoutExtension(activeFile);
                        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(baseName)) continue;
                        if (!Directory.Exists(dir)) continue;

                        try
                        {
                            foreach (var f in Directory.GetFiles(dir, baseName + "*.log"))
                                filesToProcess.Add(f);
                        }
                        catch { }
                    }
                }
            }

            // Fallback to legacy subsystem/node pattern.
            if (filesToProcess.Count == 0
                && !string.IsNullOrWhiteSpace(_logDirectory)
                && Directory.Exists(_logDirectory))
            {
                string pattern = $"{_subsystemName}_{_nodeId}*.log";
                try
                {
                    foreach (var f in Directory.GetFiles(_logDirectory, pattern))
                        filesToProcess.Add(f);
                }
                catch { }
            }

            // No source logs found - do not create an empty archive file.
            if (filesToProcess.Count == 0)
                return 0;

            var cutoff = maxAgeHours < float.MaxValue
                ? DateTime.UtcNow.AddHours(-maxAgeHours)
                : DateTime.MinValue;
            var lineCutoffLocal = maxAgeHours < float.MaxValue
                ? DateTime.Now.AddHours(-maxAgeHours)
                : DateTime.MinValue;

            int linesWritten = 0;

            var targetDir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            await using var writer = new StreamWriter(targetFilePath, append: false);

            foreach (var file in filesToProcess)
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
                    // ⭐⭐⭐ QA-010 — start TRUE: a record with no parseable timestamp cannot be shown to
                    //    be too old, so it passes.
                    //
                    // ⛔ This started FALSE, which meant every line before the first
                    //    `[yyyy-MM-dd HH:mm:ss.fff]` prefix was silently DROPPED — so a log file with no
                    //    timestamps at all archived as EMPTY, and a real file lost its header/banner
                    //    lines. 📐 Measured 2026-08-26: this is all five LogArchiveExtractionServiceTests
                    //    reds ("Expected: 3, Actual: 0"). The tests were right and the service was wrong.
                    //
                    // ⭐ It also makes the age policy agree with the severity policy one method below,
                    //    which documents itself as "lines that cannot be parsed pass through by default
                    //    (fail-safe)". The two halves of one filter disagreed.
                    //
                    // ⚠ The coarse per-FILE age filter above (last-write-time) still excludes whole stale
                    //    files, so this does not resurrect old logs — it only stops discarding the
                    //    un-timestamped prefix of a file that already qualified.
                    bool currentRecordPassesAge = true;
                    while ((line = await reader.ReadLineAsync(ct)) != null)
                    {
                        ct.ThrowIfCancellationRequested();

                        // Timestamped line starts a new record; continuation lines inherit this state.
                        if (TryParseTimestamp(line, out var recordTime))
                            currentRecordPassesAge = recordTime >= lineCutoffLocal;

                        if (!currentRecordPassesAge) continue;
                        if (!LinePassesFilter(line, severityThreshold)) continue;

                        await writer.WriteLineAsync(line.AsMemory(), ct);
                        linesWritten++;
                    }
                }
            }

            return linesWritten;
        }

        private static bool TryParseTimestamp(ReadOnlySpan<char> line, out DateTime dt)
        {
            dt = default;
            if (line.Length < 26 || line[0] != '[') return false;
            int close = line.IndexOf(']');
            if (close <= 1) return false;

            var inner = line[1..close];
            return DateTime.TryParseExact(
                inner,
                new[] { "yyyy-MM-dd HH:mm:ss.ffff", "yyyy-MM-dd HH:mm:ss.fff" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dt);
        }

        private static bool TryParseTimestamp(string line, out DateTime dt)
            => TryParseTimestamp(line.AsSpan(), out dt);

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
                int firstCloseIdx = line.IndexOf(']');
                if (firstCloseIdx > 1)
                {
                    // Primary format: [TIMESTAMP] [LEVEL] ...
                    var remainder = line.Slice(firstCloseIdx + 1).TrimStart();
                    if (!remainder.IsEmpty && remainder[0] == '[')
                    {
                        int secondCloseIdx = remainder.IndexOf(']');
                        if (secondCloseIdx > 1)
                        {
                            var levelToken = remainder.Slice(1, secondCloseIdx - 1).Trim();
                            if (int.TryParse(levelToken, out int numericLevel))
                                return numericLevel >= severityThreshold;
                            return MapNamedSeverity(levelToken) >= severityThreshold;
                        }
                    }

                    // Legacy bracket format fallback: [LEVEL] ...
                    var token = line.Slice(1, firstCloseIdx - 1).Trim();
                    if (int.TryParse(token, out int numericSev))
                        return numericSev >= severityThreshold;
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

        private static bool LinePassesFilter(string line, int severityThreshold)
            => LinePassesFilter(line.AsSpan(), severityThreshold);

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
