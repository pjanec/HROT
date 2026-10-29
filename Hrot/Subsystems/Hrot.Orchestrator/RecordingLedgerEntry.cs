using System;

namespace Hrot.Orchestrator;

/// <param name="ParentExerciseId">⭐ CE-4097 (AQ108 D5) — the exercise this run branched from (<c>Guid.Empty</c>: a run from a load, and every entry
/// written before this field — it reads as not-a-branch).</param>
/// <param name="BranchWallTicks">⭐ the branch point, in the recording metadata's own unit (<c>RecordingMetadata.BranchWallTicks</c>).</param>
public sealed record RecordingLedgerEntry(
    Guid ExerciseId,
    string? ScenarioId,
    DateTime StartTimeUtc,
    TimeSpan Duration,
    Guid ParentExerciseId = default,
    long BranchWallTicks = 0);
