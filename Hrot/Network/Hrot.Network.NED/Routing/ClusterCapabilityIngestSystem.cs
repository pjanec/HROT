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
        private readonly HashSet<int> _departed = new();

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
                    // ⭐ S7 — the durable capabilities instance is the RELIABLE half of the departure signal: every node
                    //   writes it once at join (Reliable + TransientLocal), so every reader holds the instance and its
                    //   writer's end always arrives. The BestEffort heartbeat alone was measured to miss it (1 run in 10).
                    //   ⚠ The STATE is tested before IsValid: a dispose carries no data, but when a data sample is still
                    //   UNREAD as it arrives, DDS adds no invalid sample — take reports the instance's not-alive state on
                    //   that data sample (measured: an exit while the reader was idle). Taking every frame narrows it to
                    //   a heartbeat written just before the exit; it does not close it.
                    if (IsNotAlive(sample.Info.InstanceState))
                    {
                        Departed(view, DdsTypeSupport.FromNative<NodeCapabilitiesTopic>(sample.NativePtr).NodeId,
                                 sample.Info.InstanceState);
                        continue;
                    }
                    if (!sample.IsValid) continue;
                    _nodeCapabilities[sample.Data.NodeId] = DeserializeTokens(sample.Data.CapabilitiesJson);
                }

            using var loan = _heartbeatReader.Take();
            foreach (var sample in loan)
            {
                // ⭐ S7 — the node LEFT: its heartbeat instance has one writer, so not-alive means that node crashed
                //   (lease expiry: NotAliveDisposed after ~10 s, measured) or exited. State first, then IsValid (see
                //   above). The key is read from the native buffer. 📄 docs/DESIGN_Ownership_Groups_And_Grants.md §5.6 S7.
                if (IsNotAlive(sample.Info.InstanceState))
                {
                    Departed(view, DdsTypeSupport.FromNative<NodeHeartbeat>(sample.NativePtr).NodeId, sample.Info.InstanceState);
                    continue;
                }
                if (!sample.IsValid) continue;
                _departed.Remove(sample.Data.NodeId);   // a node that comes back can leave again
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

        private static bool IsNotAlive(DdsInstanceState state)
            => state == DdsInstanceState.NotAliveDisposed || state == DdsInstanceState.NotAliveNoWriters;

        /// <summary>
        /// A node left: forget it and tell the ownership reclaim (R-167 — every node sees the same departure and takes
        /// back the same keys, no message). Raised once per node per departure (both topics report it), and also for a
        /// node the cache never knew — a record can name a node no grant strategy chose (a test hook, an external
        /// hand-in). A repeat moves nothing.
        /// </summary>
        private void Departed(ISimulationView view, int nodeId, DdsInstanceState state)
        {
            if (!_departed.Add(nodeId)) return;
            _cache.RemoveNode(nodeId);
            _nodeCapabilities.Remove(nodeId);
            Fdp.Core.Logging.FdpLog<ClusterCapabilityIngestSystem>.Info(
                "[ClusterCapabilityIngest] node {0} left ({1}).", nodeId, state);
            if (view is EntityRepository repo)
                repo.Bus.Publish(new Fdp.Toolkit.Replication.Messages.NodeDeparted { NodeId = nodeId });
        }

        private static string[] DeserializeTokens(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
            try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }
    }
}
