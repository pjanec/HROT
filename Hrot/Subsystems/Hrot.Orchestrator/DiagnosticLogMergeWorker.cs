using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Hrot.Orchestrator.Events;

namespace Hrot.Orchestrator;

/// <summary>
/// Performs a K-way chronological merge of per-node diagnostic log files.
///
/// <para>
/// Subscribes to <see cref="MergeLogsIntent"/> via <see cref="FdpEventBus.ReadManaged{T}"/>
/// in <see cref="Tick"/>. Each intent spawns a <c>LongRunning</c> background task that
/// opens one <see cref="StreamReader"/> per source file, merges them by timestamp using a
/// min-heap, and writes the sorted output to a single merged file on the NAS.
/// On success, publishes <see cref="LogMergeCompletedEvent"/> to the bus.
/// </para>
///
/// <para>
/// Continuation lines (stack traces, multi-line messages) — lines without a leading
/// <c>[YYYY-MM-DD HH:mm:ss.ffff]</c> timestamp — are written immediately after the
/// originating log record, preserving context.
/// </para>
///
/// <para>Call <see cref="Tick"/> once per frame after <see cref="ClusterMaster.Tick"/>.</para>
/// </summary>
public sealed class DiagnosticLogMergeWorker : IDisposable
{
    private readonly FdpEventBus _bus;
    private CancellationTokenSource? _cts;

    /// <param name="bus">Shared event bus used to read <see cref="MergeLogsIntent"/>
    /// and publish <see cref="LogMergeCompletedEvent"/>.</param>
    public DiagnosticLogMergeWorker(FdpEventBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    /// <summary>
    /// Drains pending <see cref="MergeLogsIntent"/> events and spawns a merge task for each.
    /// Call once per frame in Phase 3, after <see cref="ClusterMaster.Tick"/>.
    /// </summary>
    public void Tick()
    {
        foreach (var intent in _bus.ReadManaged<MergeLogsIntent>())
        {
            // Cancel any in-progress merge before starting a new one.
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var capturedToken = _cts.Token;
            var capturedIntent = intent;
            Task.Factory.StartNew(
                () => DoMerge(capturedIntent, capturedToken),
                capturedToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    // ── Merge implementation ─────────────────────────────────────────────────

    private void DoMerge(MergeLogsIntent intent, CancellationToken ct)
    {
        var outputPath = Path.Combine(
            intent.NasBasePath,
            "dumps",
            $"dump_{intent.DumpTimestamp}_logs_MERGED.log");

        try
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
        catch { /* best-effort; will fail visibly at StreamWriter open */ }

        var readers = new List<StreamReader>();
        try
        {
            StreamWriter? output;
            try
            {
                output = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // Cannot open output file — publish nothing, silently bail.
                _ = ex;
                return;
            }

            using (output)
            {
                foreach (var relPath in intent.LogRelativePaths)
                {
                    if (ct.IsCancellationRequested) return;
                    var fullPath = Path.Combine(intent.NasBasePath, relPath);
                    try
                    {
                        readers.Add(new StreamReader(fullPath, System.Text.Encoding.UTF8));
                    }
                    catch (Exception ex)
                    {
                        output.WriteLine($"[MERGE WARNING] Cannot open {fullPath}: {ex.Message}");
                    }
                }

                MergeReadersCore(readers, output, ct);
            }
        }
        finally
        {
            foreach (var r in readers)
                r.Dispose();
        }

        if (!ct.IsCancellationRequested)
            _bus.PublishManaged(new LogMergeCompletedEvent { NasPath = outputPath });
    }

    // ── K-way merge (internal, testable) ────────────────────────────────────

    /// <summary>
    /// Merges <paramref name="readers"/> into <paramref name="output"/> in timestamp order.
    /// Continuation lines (no leading timestamp) are written immediately after their originating record.
    /// Exposed as <c>internal</c> for unit testing.
    /// </summary>
    internal static void MergeReadersCore(
        IEnumerable<TextReader> readers,
        TextWriter output,
        CancellationToken ct)
    {
        var queue = new PriorityQueue<(string Line, TextReader Reader), DateTime>();

        foreach (var reader in readers)
        {
            if (ct.IsCancellationRequested) return;
            SeekFirstTimestampedLine(reader, queue);
        }

        while (queue.Count > 0 && !ct.IsCancellationRequested)
        {
            var (line, reader) = queue.Dequeue();
            output.WriteLine(line);
            ReadContinuationAndNextTimestamp(reader, queue, output, ct);
        }
    }

    /// <summary>Advances <paramref name="reader"/> to the first timestamped line and enqueues it.</summary>
    private static void SeekFirstTimestampedLine(
        TextReader reader,
        PriorityQueue<(string, TextReader), DateTime> queue)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (TryParseTimestamp(line.AsSpan(), out var dt))
            {
                queue.Enqueue((line, reader), dt);
                return;
            }
            // Non-timestamped leading lines are discarded (e.g. file headers).
        }
    }

    /// <summary>
    /// Writes any continuation lines from <paramref name="reader"/> to <paramref name="output"/>,
    /// then enqueues the next timestamped line (if any).
    /// </summary>
    private static void ReadContinuationAndNextTimestamp(
        TextReader reader,
        PriorityQueue<(string, TextReader), DateTime> queue,
        TextWriter output,
        CancellationToken ct)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (ct.IsCancellationRequested) return;
            if (TryParseTimestamp(line.AsSpan(), out var dt))
            {
                queue.Enqueue((line, reader), dt);
                return;
            }
            // Continuation line (stack trace, multi-line message).
            output.WriteLine(line);
        }
        // EOF: reader exhausted; do not re-enqueue.
    }

    // ── Timestamp parser ─────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to parse a timestamp from the start of <paramref name="line"/>.
    /// Expected format: <c>[YYYY-MM-DD HH:mm:ss.ffff]</c> or <c>[YYYY-MM-DD HH:mm:ss.fff]</c>.
    /// </summary>
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

    /// <inheritdoc/>
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
