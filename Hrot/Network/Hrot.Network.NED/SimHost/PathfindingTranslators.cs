using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;
using Fdp.ModuleHost.Core.Abstractions;

namespace Hrot.Network.NED.SimHost
{
    // ── Brain-side pathfinding translators (Brain → NavigationSolver) ─────────────

    /// <summary>
    /// Stub egress translator. Publishes <c>PathRequestBatch</c> (Brain → NavigationSolver).
    /// Full implementation is deferred to a future batch.
    /// </summary>
    public sealed class PathRequestBrainEgressTranslator : IDescriptorTranslator
    {
        public long   DescriptorOrdinal => 64;
        public string TopicName         => "PathRequestBatch";

        public PathRequestBrainEgressTranslator(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Stub ingress translator. Receives <c>PathResponseBatch</c> from the NavigationSolver.
    /// </summary>
    public sealed class PathResponseBrainIngressTranslator : IDescriptorTranslator
    {
        public long   DescriptorOrdinal => 65;
        public string TopicName         => "PathResponseBatch";

        public PathResponseBrainIngressTranslator(
            DdsParticipant   participant,
            NetworkEntityMap entityMap) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    // ── Solver-side pathfinding translators (NavigationSolver → Brain) ────────────

    /// <summary>
    /// Stub ingress translator. Receives <c>PathRequestBatch</c> from Brain nodes.
    /// </summary>
    public sealed class PathRequestSolverIngressTranslator : IDescriptorTranslator
    {
        public long   DescriptorOrdinal => 64;
        public string TopicName         => "PathRequestBatch";

        public PathRequestSolverIngressTranslator(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Stub egress translator. Publishes <c>PathResponseBatch</c> (NavigationSolver → Brain).
    /// </summary>
    public sealed class PathResponseSolverEgressTranslator : IDescriptorTranslator
    {
        public long   DescriptorOrdinal => 65;
        public string TopicName         => "PathResponseBatch";

        public PathResponseSolverEgressTranslator(
            DdsParticipant   participant,
            NetworkEntityMap entityMap) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
