using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.SimHost
{
    // ── Brain-side EQS translators (Brain -> Muscle, Muscle -> Brain) ────────────

    /// <summary>
    /// Brain egress translator: reads <see cref="AreaQueryRequestEvent"/>s published by
    /// Brain BTree nodes onto the <see cref="FdpEventBus"/>, converts each to a
    /// <see cref="DdsAreaQueryRequest"/> and publishes a single
    /// <see cref="AreaQueryRequestBatch"/> to the Muscle node via DDS.
    /// Reads from the previous frame's event buffer (1-frame latency is negligible
    /// given the 100 ms solver cycle on the Muscle).
    /// Only forwards requests whose <c>SourceNodeId</c> matches <c>_localNodeId</c>.
    /// </summary>
    public sealed class AreaQueryBrainEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "AreaQueryRequestBatch";

        private readonly IDdsWriter<AreaQueryRequestBatch> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly int _localNodeId;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtAreaQueryRequestBatch;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public AreaQueryBrainEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap, int localNodeId)
            : this(new DdsWriterAdapter<AreaQueryRequestBatch>(participant, DdsTopicName), entityMap, localNodeId)
        {
        }

        /// <summary>Internal test constructor — accepts a stub writer.</summary>
        internal AreaQueryBrainEgressTranslator(
            IDdsWriter<AreaQueryRequestBatch> writer,
            NetworkEntityMap entityMap,
            int localNodeId)
        {
            _writer      = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap   = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            // Read accumulated request events from the previous frame's write buffer.
            var requests = view.ReadEvents<AreaQueryRequestEvent>();
            if (requests.IsEmpty) return;

            var ddsRequests = new List<DdsAreaQueryRequest>(requests.Length);
            for (int i = 0; i < requests.Length; i++)
            {
                ref readonly var req = ref requests[i];

                // Authority filter: only forward requests originating from this node.
                if (req.SourceNodeId != _localNodeId) continue;

                if (!_entityMap.TryGetNetworkId(req.TargetAreaEntity, out long areaNetworkId)) continue;

                ddsRequests.Add(new DdsAreaQueryRequest
                {
                    RequestId           = req.RequestId,
                    TargetAreaNetworkId = areaNetworkId,
                    SourceNodeId        = req.SourceNodeId,
                    ForceId             = (int)req.TargetForce,
                });
            }

            if (ddsRequests.Count == 0) return;

            _writer.Write(new AreaQueryRequestBatch
            {
                SourceNodeId = _localNodeId,
                Requests     = ddsRequests,
            });
            SentSampleCount++;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }

    // ── Muscle-side ingress: receives requests from Brain, publishes request events ──

    /// <summary>
    /// Muscle ingress translator: receives <see cref="AreaQueryRequestBatch"/> from Brain
    /// nodes via DDS and publishes <see cref="AreaQueryRequestEvent"/>s via
    /// <see cref="IEntityCommandBuffer"/> so <c>AreaQuerySolverSystem</c> can resolve them
    /// on its next background tick.
    /// If the area polygon entity cannot be resolved via <see cref="NetworkEntityMap"/>,
    /// publishes an immediate <see cref="AreaQueryResultEvent"/> with <c>TargetCount = 0</c>.
    /// </summary>
    public sealed class AreaQueryMuscleIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "AreaQueryRequestBatch";

        private readonly DdsReader<AreaQueryRequestBatch>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtAreaQueryRequestBatch;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        /// <summary>
        /// Production constructor. Pass <c>null</c> for <paramref name="participant"/>
        /// in unit tests; <see cref="PollIngress"/> becomes a no-op.
        /// </summary>
        public AreaQueryMuscleIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null
                ? new DdsReader<AreaQueryRequestBatch>(participant, DdsTopicName)
                : null;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                ProcessBatch(sample.Data, cmd, view);
            }
        }

        /// <summary>
        /// Processes a single <see cref="AreaQueryRequestBatch"/> sample.
        /// Exposed as <c>internal</c> for unit test injection.
        /// </summary>
        internal void ProcessBatch(in AreaQueryRequestBatch batch, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (batch.Requests == null || batch.Requests.Count == 0) return;

            if (view is not EntityRepository repo) return;

            foreach (var ddsReq in batch.Requests)
            {
                // Resolve the area polygon entity on this Muscle node.
                if (!_entityMap.TryGetEntity(ddsReq.TargetAreaNetworkId, out var areaEntity))
                {
                    // Area entity not known on this Muscle — emit an immediate empty result event
                    // so the Brain BTree does not stall waiting for a response that will never come.
                    if (!repo.HasSingleton<EqsTargetPool>()) continue;
                    var pool = repo.GetSingleton<EqsTargetPool>();
                    cmd.PublishEvent(new AreaQueryResultEvent
                    {
                        RequestId            = ddsReq.RequestId,
                        TargetCount          = 0,
                        TargetGroupHandle    = -1,
                        SourceNodeId         = ddsReq.SourceNodeId,
                        NewPoolNextFreeIndex = pool.NextFreeIndex,
                    });
                    continue;
                }

                // Queue the request for the background solver.
                cmd.PublishEvent(new AreaQueryRequestEvent
                {
                    RequestId        = ddsReq.RequestId,
                    TargetAreaEntity = areaEntity,
                    TargetForce      = (ForceId)ddsReq.ForceId,
                    SourceNodeId     = ddsReq.SourceNodeId,
                });
            }
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }

    // ── Muscle-side egress: reads result events, sends responses to Brain ─────────

    /// <summary>
    /// Muscle egress translator: reads resolved <see cref="AreaQueryResultEvent"/>s from the
    /// <see cref="FdpEventBus"/>, extracts entity handles from <see cref="EqsTargetPool"/>,
    /// resolves them to network IDs, and publishes <see cref="AreaQueryResponseBatch"/>
    /// messages grouped by <c>SourceNodeId</c> to the Brain node via DDS.
    /// </summary>
    public sealed class AreaQueryMuscleEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "AreaQueryResponseBatch";

        private readonly IDdsWriter<AreaQueryResponseBatch> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtAreaQueryResponseBatch;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        /// <summary>Production constructor — creates a live DDS writer.</summary>
        public AreaQueryMuscleEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<AreaQueryResponseBatch>(participant, DdsTopicName), entityMap)
        {
        }

        /// <summary>Internal test constructor — accepts a stub writer.</summary>
        internal AreaQueryMuscleEgressTranslator(
            IDdsWriter<AreaQueryResponseBatch> writer,
            NetworkEntityMap entityMap)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view)
        {
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<EqsTargetPool>()) return;

            var results = view.ReadEvents<AreaQueryResultEvent>();
            if (results.IsEmpty) return;

            var pool = repo.GetSingleton<EqsTargetPool>();

            // Group results by originating Brain node.
            var responsesByNode = new Dictionary<int, List<DdsAreaQueryResponse>>(results.Length);

            for (int i = 0; i < results.Length; i++)
            {
                ref readonly var evt = ref results[i];
                var response = BuildResponse(in evt, in pool);

                if (!responsesByNode.TryGetValue(evt.SourceNodeId, out var list))
                {
                    list = new List<DdsAreaQueryResponse>();
                    responsesByNode[evt.SourceNodeId] = list;
                }
                list.Add(response);
            }

            foreach (var kv in responsesByNode)
            {
                _writer.Write(new AreaQueryResponseBatch
                {
                    TargetNodeId = kv.Key,
                    Responses    = kv.Value,
                });
                SentSampleCount++;
            }
        }

        private DdsAreaQueryResponse BuildResponse(in AreaQueryResultEvent evt, in EqsTargetPool pool)
        {
            var networkIds = new List<long>(evt.TargetCount);

            if (evt.TargetGroupHandle >= 0)
            {
                for (int t = 0; t < evt.TargetCount; t++)
                {
                    int poolIdx = evt.TargetGroupHandle + t;
                    if (poolIdx >= pool.Targets.Length) break;

                    long packed = pool.Targets[poolIdx];
                    if (packed == 0L) break;

                    var entity = new Entity((ulong)packed);
                    if (!_entityMap.TryGetNetworkId(entity, out long networkId)) continue;

                    networkIds.Add(networkId);
                }
            }

            return new DdsAreaQueryResponse
            {
                RequestId        = evt.RequestId,
                TargetCount      = networkIds.Count,
                TargetNetworkIds = networkIds,
            };
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }

    // ── Brain-side ingress: receives responses from Muscle, writes local results ───

    /// <summary>
    /// Brain ingress translator: receives <see cref="AreaQueryResponseBatch"/> from the
    /// Muscle node via DDS, resolves network IDs back to local <see cref="Entity"/>
    /// handles, writes them into the Brain's <see cref="EqsTargetPool"/>, and sets
    /// <see cref="AreaQueryResult.IsReady"/> in the <see cref="AreaQueryBatchData"/> ring
    /// buffer directly (runs on the main thread so direct writes are safe).
    /// The ring-buffer slot is derived from <c>requestId % DefaultCapacity</c>.
    /// </summary>
    public sealed class AreaQueryBrainIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "AreaQueryResponseBatch";

        private readonly DdsReader<AreaQueryResponseBatch>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly int _localNodeId;

        public string TopicName         => DdsTopicName;
        public long   DescriptorOrdinal => (long)EDescriptorType.dtAreaQueryResponseBatch;
        public long   ReceivedSampleCount { get; private set; }
        public long   SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        /// <summary>
        /// Production constructor. Pass <c>null</c> for <paramref name="participant"/>
        /// in unit tests; <see cref="PollIngress"/> becomes a no-op.
        /// </summary>
        public AreaQueryBrainIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, int localNodeId = 0)
        {
            _entityMap   = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
            _reader      = participant is not null
                ? new DdsReader<AreaQueryResponseBatch>(participant, DdsTopicName)
                : null;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            if (view is not EntityRepository repo) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;

                // Network routing: only accept responses addressed to this Brain node.
                if (data.TargetNodeId != _localNodeId && data.TargetNodeId != 0) continue;

                ProcessBatch(data, repo);
            }
        }

        /// <summary>
        /// Processes a single <see cref="AreaQueryResponseBatch"/> sample.
        /// Exposed as <c>internal</c> for unit test injection.
        /// Runs on the main thread — direct writes to NativeArray and struct singletons are safe.
        /// </summary>
        internal void ProcessBatch(in AreaQueryResponseBatch batch, EntityRepository repo)
        {
            if (batch.Responses == null || batch.Responses.Count == 0) return;
            if (!repo.HasSingleton<AreaQueryBatchData>()) return;
            if (!repo.HasSingleton<EqsTargetPool>()) return;

            ref var localBatch = ref repo.GetSingleton<AreaQueryBatchData>();
            ref var pool       = ref repo.GetSingleton<EqsTargetPool>();

            foreach (var response in batch.Responses)
            {
                // Use the same XOR-hash slot formula as AreaQueryBatchHelper.ComputeSlot and
                // AreaQueryResultMaterializationSystem.ComputeSlot so the brain ingress writes
                // to the same ring-buffer slot that GetAreaQueryResult() will later read.
                int slot = (int)(((ulong)response.RequestId ^ ((ulong)response.RequestId >> 32)) % (uint)AreaQueryBatchData.DefaultCapacity);

                int groupHandle = -1;
                int resolvedCount = 0;

                if (response.TargetNetworkIds != null && response.TargetNetworkIds.Count > 0)
                {
                    groupHandle = pool.NextFreeIndex;

                    foreach (long networkId in response.TargetNetworkIds)
                    {
                        int poolIdx = pool.NextFreeIndex;
                        if (poolIdx >= pool.Targets.Length) break;

                        if (!_entityMap.TryGetEntity(networkId, out var entity)) continue;

                        pool.Targets[poolIdx]  = (long)entity.PackedValue;
                        pool.NextFreeIndex++;
                        resolvedCount++;
                    }

                    if (resolvedCount == 0) groupHandle = -1;
                }

                localBatch.Results[slot] = new AreaQueryResult
                {
                    RequestId         = response.RequestId,
                    TargetCount       = resolvedCount,
                    TargetGroupHandle = groupHandle,
                    SourceNodeId      = _localNodeId,
                    IsReady           = true,
                };
            }

            // Write back the pool (value copy) so NextFreeIndex is persisted.
            repo.SetSingleton(pool);
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }
}
