namespace Hrot.CGF.Systems.Routing;

/// <summary>
/// Named byte offsets into <see cref="Fdp.Toolkit.Behavior.Components.BrainBlackboard.Memory"/>
/// reserved for route "soft advice" values written by
/// <see cref="Hrot.CGF.Systems.Routing.RouteContextSystem"/>.
///
/// <para>
/// Soft-advice values occupy the SoftAdvice region of
/// <see cref="Fdp.Toolkit.Behavior.Components.BlackboardMemoryLayout"/> (bytes 60-125),
/// well clear of the behavior parameter payload (bytes 0-59) and the interrupt registers
/// (bytes 126-127).
/// </para>
/// </summary>
public static class BlackboardOffsets
{
    /// <summary>
    /// Byte offset for the per-waypoint threat/danger level
    /// (JSON key: <c>"dangerLevel"</c>).
    /// A value of 0 means unknown/default; higher values indicate increasing danger.
    /// Placed at the start of the SoftAdvice region (offset 60) + 60 additional bytes = 120.
    /// </summary>
    public const int ExpectedThreatLevel =
        Fdp.Toolkit.Behavior.BehaviorConstants.MaxBehaviorParamByteSize + 60;
}
