namespace Hrot.CGF.Systems.Routing;

/// <summary>
/// Named byte offsets into <see cref="Fdp.Toolkit.Behavior.Components.BrainBlackboard"/>
/// reserved for route "soft advice" values written by
/// <see cref="Hrot.CGF.Systems.Routing.RouteContextSystem"/>.
/// </summary>
public static class BlackboardOffsets
{
    /// <summary>
    /// Byte offset for the per-waypoint threat/danger level
    /// (JSON key: <c>"dangerLevel"</c>).
    /// A value of 0 means unknown/default; higher values indicate increasing danger.
    /// Mirrors <see cref="Fdp.Toolkit.Behavior.BehaviorConstants.ExpectedThreatLevel_Offset"/>.
    /// </summary>
    public const int ExpectedThreatLevel =
        Fdp.Toolkit.Behavior.BehaviorConstants.ExpectedThreatLevel_Offset;
}
