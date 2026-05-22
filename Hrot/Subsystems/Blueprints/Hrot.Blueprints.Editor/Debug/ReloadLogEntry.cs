namespace Hrot.Blueprints.Editor.Debug;

public sealed record ReloadLogEntry(
    DateTime Timestamp,
    ReloadSource Source,
    bool Succeeded,
    string? Message,
    long DurationMs);
