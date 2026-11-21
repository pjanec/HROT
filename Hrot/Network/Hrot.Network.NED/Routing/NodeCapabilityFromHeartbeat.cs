using System.Collections.Generic;
using Fdp.Core;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Network.Routing;

/// <summary>
/// ⭐ CE-4075 — the ONE mapping of a node's heartbeat sample (plus its advertised capability tokens) to the cluster cache's
/// <see cref="NodeCapability"/>. ⛔ It used to be written out in <c>NedNetworkFactory</c> AND <c>ClusterCapabilityIngestSystem</c>.
/// </summary>
public static class NodeCapabilityFromHeartbeat
{
    private static readonly HashSet<string> EmptyFeatures = new();

    public static NodeCapability Map(in NodeHeartbeat hb, string[] tokens) => new()
    {
        NodeId             = (Fdp.Core.NodeId)(hb.NodeId),
        // CE-286 (C-roles): the role mask is DERIVED from the fdp.role.* subset of the node's capability tokens (AQ-70 §Q70-C) —
        // the heartbeat no longer carries RolesMask. An un-advertising / foreign node reads None; multi-role [Flags] masks survive.
        Role               = NodeRoleTokens.MaskFromTokens(tokens),
        Capabilities       = new HashSet<string>(tokens),
        FeaturesOn         = hb.FeaturesOn is { Count: > 0 } on ? new HashSet<string>(on) : EmptyFeatures,   // ⭐ Player Join §9.1
        RamUsedBytes       = hb.RamUsedBytes,
        LastSeenUtcSeconds = (double)hb.WallTicksUtc / System.TimeSpan.TicksPerSecond,
    };
}
