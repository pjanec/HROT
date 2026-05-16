using System;

namespace Hrot.Orchestrator;

public sealed record RecordingLedgerEntry(
    Guid ExerciseId,
    string? ScenarioId,
    DateTime StartTimeUtc,
    TimeSpan Duration);
