using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Network.Routing
{
    /// <summary>
    /// CE-291 (piece C) — the SHARED cluster-membership ingest: reads the durable <see cref="NodeCapabilitiesTopic"/>
    /// and <see cref="NodeHeartbeat"/> topics into the node's <see cref="SimpleClusterStateCache"/> every tick, so
    /// EVERY ECS node — not just CGF — knows which peers advertise which capabilities.
    ///
    /// <para>⭐ <b>Why this is a shared SYSTEM, not per-host wiring.</b> 🔒 User ruling (<c>2026-09-16</c>):
    /// <i>"if something should work same way on all ecs enabled nodes it should live in shared code that is called
    /// from individual hosts; the node-centric gating is obsolete."</i> The reliable-init barrier's wait-set
    /// (<c>ClusterCacheExpectedPeersProvider</c>) reads this cache; before this, only CGF drove the ingest
    /// (via <c>NedCgfEntityLifecycleAdapters.PollNetwork</c>), so a SimHost/IG/Stride creator saw an empty cache
    /// and never engaged the barrier. Registered by <c>NedReplicationModule</c> — which every ECS node builds via
    /// the shared bootstrapper — this makes any node a symmetric reliable creator
    /// (DESIGN_Cross_Node_Construction_Barrier.md §3a.4 "any node can be creator or peer").</para>
    ///
    /// <para>⚠ The derivation mirrors <c>NedCgfEntityLifecycleAdapters.PollNetwork</c> exactly: the role mask is
    /// DERIVED from the <c>fdp.role.*</c> subset of each node's tokens (CE-286), the heartbeat carries no RolesMask.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class ClusterCapabilityIngestSystem : IEcsModuleSystem
    {
        private readonly DdsReader<NodeHeartbeat> _heartbeatReader;
        private readonly DdsReader<NodeCapabilitiesTopic> _capabilitiesReader;
        private readonly SimpleClusterStateCache _cache;
        private readonly Dictionary<int, string[]> _nodeCapabilities = new();

        public ClusterCapabilityIngestSystem(
            DdsReader<NodeHeartbeat> heartbeatReader,
            DdsReader<NodeCapabilitiesTopic> capabilitiesReader,
            SimpleClusterStateCache cache)
        {
            _heartbeatReader    = heartbeatReader ?? throw new ArgumentNullException(nameof(heartbeatReader));
            _capabilitiesReader = capabilitiesReader ?? throw new ArgumentNullException(nameof(capabilitiesReader));
            _cache              = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            // Gather the durable capability tokens FIRST, so the heartbeat build below derives the role mask
            // from the latest known token set. Retained samples arrive immediately for present + late nodes.
            using (var capLoan = _capabilitiesReader.Take())
                foreach (var sample in capLoan)
                {
                    if (!sample.IsValid) continue;
                    _nodeCapabilities[sample.Data.NodeId] = DeserializeTokens(sample.Data.CapabilitiesJson);
                }

            using var loan = _heartbeatReader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var tokens = _nodeCapabilities.TryGetValue(sample.Data.NodeId, out var t) ? t : Array.Empty<string>();
                _cache.UpdateNode(new NodeCapability
                {
                    NodeId             = sample.Data.NodeId,
                    Role               = NodeRoleTokens.MaskFromTokens(tokens),
                    Capabilities       = new HashSet<string>(tokens),
                    CpuUsagePercent    = sample.Data.CpuUsagePercent,
                    RamUsedBytes       = sample.Data.RamUsedBytes,
                    LastSeenUtcSeconds = (double)sample.Data.WallTicksUtc / TimeSpan.TicksPerSecond,
                });
            }
        }

        private static string[] DeserializeTokens(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
            try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }
    }
}
