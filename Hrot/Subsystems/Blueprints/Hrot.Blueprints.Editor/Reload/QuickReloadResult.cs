namespace Hrot.Blueprints.Editor.Reload;

public sealed record QuickReloadResult(
    bool Succeeded,
    string? ErrorMessage,
    long DurationMs);
