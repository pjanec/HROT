namespace Hrot.CGF.Systems.Routing;

/// <summary>
/// Named byte offsets into <see cref="Fdp.Toolkit.Behavior.Components.BrainBlackboard.Memory"/>
/// reserved for route "soft advice" values written by
/// <see cref="Hrot.CGF.Systems.Routing.RouteContextSystem"/>.
///
/// <para>
/// Soft-advice values occupy the high end of the 128-byte
/// <see cref="Fdp.Toolkit.Behavior.BehaviorConstants.BrainBlackboardByteSize"/> buffer,
/// well clear of the doctrine parameter structs that populate offsets 0-15.
/// </para>
/// </summary>
public static class BlackboardOffsets
{
    /// <summary>
    /// Byte offset for the per-waypoint threat/danger level
    /// (JSON key: <c>"dangerLevel"</c>).
    /// A value of 0 means unknown/default; higher values indicate increasing danger.
    /// </summary>
    public const int ExpectedThreatLevel = 120;
}
