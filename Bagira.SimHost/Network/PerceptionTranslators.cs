using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Bagira.SimHost.Network
{
    // ── Brain-side perception translators (Brain → Perception Solver) ─────────────

    /// <summary>
    /// Stub egress translator. Publishes <c>SensorConfig</c> for owned entities (Brain → Solver).
    /// Full implementation is deferred to a future batch; this stub compiles and is safe to
    /// wire without an active DDS participant.
    /// </summary>
    public sealed class SensorConfigEgressTranslator : IDescriptorTranslator
    {
        public long   DescriptorOrdinal => 60;
        public string TopicName         => "SensorConfig";

        public SensorConfigEgressTranslator(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Stub egress translator. Publishes <c>RaycastRequestBatch</c> (Brain → Solver).
    /// </summary>
    public sealed class RaycastBatchEgressTranslator : IDescriptorTranslator
    {
        public long   DescriptorOrdinal => 61;
        public string TopicName         => "RaycastRequestBatch";

        public RaycastBatchEgressTranslator(
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Stub ingress translator. Receives <c>SensorTargets</c> published by the Solver.
    /// </summary>
    public sealed class SensorTargetsIngressTranslator : IDescriptorTranslator
    {
        public long   DescriptorOrdinal => 62;
        public string TopicName         => "SensorTargets";

        public SensorTargetsIngressTranslator(
            DdsParticipant   participant,
            NetworkEntityMap entityMap) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Stub ingress translator. Receives <c>RaycastResponseBatch</c> published by the Solver.
    /// </summary>
    public sealed class RaycastBatchIngressTranslator : IDescriptorTranslator
    {
        public long   DescriptorOrdinal => 63;
        public string TopicName         => "RaycastResponseBatch";

        public RaycastBatchIngressTranslator(
            DdsParticipant   participant,
            NetworkEntityMap entityMap) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    // ── Solver-side perception translators (Perception Solver → Brain) ────────────

    /// <summary>
    /// Stub ingress translator. Receives <c>SensorConfig</c> from the Brain node.
    /// </summary>
    public sealed class SensorConfigIngressTranslator : IDescriptorTranslator
    {
        public long   DescriptorOrdinal => 60;
        public string TopicName         => "SensorConfig";

        public SensorConfigIngressTranslator(
            DdsParticipant   participant,
            NetworkEntityMap entityMap) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Stub ingress translator. Receives <c>RaycastRequestBatch</c> from the Brain node.
    /// </summary>
    public sealed class RaycastBatchSolverIngressTranslator : IDescriptorTranslator
    {
        public long   DescriptorOrdinal => 61;
        public string TopicName         => "RaycastRequestBatch";

        public RaycastBatchSolverIngressTranslator(
            DdsParticipant   participant,
            NetworkEntityMap entityMap) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Stub egress translator. Publishes <c>SensorTargets</c> to Brain nodes.
    /// </summary>
    public sealed class SensorTargetsEgressTranslator : IDescriptorTranslator
    {
        public long   DescriptorOrdinal => 62;
        public string TopicName         => "SensorTargets";

        public SensorTargetsEgressTranslator(
            DdsParticipant   participant,
            NetworkEntityMap entityMap) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Stub egress translator. Publishes <c>RaycastResponseBatch</c> (Solver → Brain).
    /// </summary>
    public sealed class RaycastBatchSolverEgressTranslator : IDescriptorTranslator
    {
        public long   DescriptorOrdinal => 63;
        public string TopicName         => "RaycastResponseBatch";

        public RaycastBatchSolverEgressTranslator(
            DdsParticipant   participant,
            NetworkEntityMap entityMap) { }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
