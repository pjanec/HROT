using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using NedClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.Common.Orchestration;

/// <summary>
/// ⭐⭐ <c>CE-3345</c> part B (AQ110 A) — <b>the ONE mapping of a node's heartbeat between the bus event and the DDS <see cref="NodeHeartbeat"/></b>: the slave's egress writes it, the master's ingress and the
/// observer's read it. ⛔ It used to be written out three times (egress, <c>NedOrchestrationTranslator</c>, <c>OrchestrationObserverTranslator</c>); a field added to the heartbeat (the node's phase, step and
/// failure reason) is added HERE once, and a round-trip test pins both halves.
/// </summary>
public static class NodeHeartbeatWire
{
    /// <summary>The bus event as the DDS sample. ⚠ The CPU / RAM / tick-advancing fields are telemetry the slave does not measure (zero), as before.</summary>
    public static NodeHeartbeat ToTopic(in NodeHeartbeatEvent hb) => new()
    {
        NodeId            = hb.NodeId,
        SessionId         = hb.SessionId,   // ⭐ C2
        SubsystemName     = hb.SubsystemName ?? string.Empty,
        LocalClusterState = (NedClusterState)hb.LocalStateId,
        WallTicksUtc      = hb.WallTicksUtc,
        CpuUsage          = 0f,
        RamUsedBytes      = 0L,
        SimTickAdvancing  = false,
        SubsystemsJson    = string.Empty,
        Phase             = (int)hb.Phase,                 // ⭐ CE-3345 (AQ110 A)
        Step              = hb.Step ?? string.Empty,
        Reason            = hb.Reason ?? string.Empty,
        NodeName          = hb.NodeName ?? string.Empty,   // ⭐ CE-3354 ②
        // CE-286 (C-roles): heartbeat no longer carries the role mask — roles travel as fdp.role.* capability tokens on the durable NodeCapabilities descriptor.
    };

    /// <summary>The DDS sample as the bus event. ⭐ A phase value this build does not know (a newer peer) reads as <see cref="NodePhase.Ready"/>.</summary>
    public static NodeHeartbeatEvent ToEvent(in NodeHeartbeat hb) => new()
    {
        NodeId        = hb.NodeId,
        LocalStateId  = (int)hb.LocalClusterState,
        WallTicksUtc  = hb.WallTicksUtc,
        SubsystemName = hb.SubsystemName ?? string.Empty,
        SessionId     = hb.SessionId,   // ⭐ C2 — the session the node follows
        Phase         = hb.Phase is >= (int)NodePhase.Ready and <= (int)NodePhase.Failed ? (NodePhase)hb.Phase : NodePhase.Ready,   // ⭐ CE-3345
        Step          = string.IsNullOrEmpty(hb.Step) ? null : hb.Step,
        Reason        = string.IsNullOrEmpty(hb.Reason) ? null : hb.Reason,
        NodeName      = string.IsNullOrEmpty(hb.NodeName) ? null : hb.NodeName,   // ⭐ CE-3354 ②
    };
}
