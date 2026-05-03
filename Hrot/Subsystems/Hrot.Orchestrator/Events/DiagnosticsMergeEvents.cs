using Fdp.Core;

namespace Hrot.Orchestrator.Events;

/// <summary>Triggers the K-way merge of all per-node log files from the last diagnostic dump.</summary>
[EventId(9059)]
[DataPolicy(DataPolicy.NoRecord)]
public struct MergeLogsIntent
{
    /// <summary>NAS-relative paths of the log files to merge (RelativeDest values for .log entries).</summary>
    public string[] LogRelativePaths { get; init; }

    /// <summary>NAS base path used to resolve full file paths.</summary>
    public string NasBasePath { get; init; }

    /// <summary>Timestamp string from the original dump (e.g. "20260503_120000").</summary>
    public string DumpTimestamp { get; init; }
}

/// <summary>Published by <see cref="DiagnosticLogMergeWorker"/> when the merged log file is ready.</summary>
[EventId(9060)]
[DataPolicy(DataPolicy.NoRecord)]
public struct LogMergeCompletedEvent
{
    /// <summary>Full NAS path of the merged log file.</summary>
    public string NasPath { get; init; }
}
