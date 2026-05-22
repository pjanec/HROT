namespace Hrot.Blueprints.Editor.Reload;

public sealed record FullRebuildResult(
    bool Succeeded,
    int ExitCode,
    long DurationMs);
