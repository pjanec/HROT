namespace Hrot.Editor.AiShared.Debug;

public sealed record Breakpoint(
    BreakpointId Id,
    Guid AssetId,
    Guid ElementId,
    int HitCount,
    bool Enabled,
    string DisplayName);
