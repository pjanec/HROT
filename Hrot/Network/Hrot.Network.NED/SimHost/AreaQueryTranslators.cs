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
    /// Brain egress translator: reads pending <see cref="AreaQueryRequest"/>s from the
    /// local <see cref="AreaQueryBatchData"/> singleton, converts each to a
    /// <see cref="DdsAreaQueryRequest"/> and publishes a single
    /// <see cref="AreaQueryRequestBatch"/> to the Muscle node via DDS.
    /// Clears <c>batch.Count</c> after publishing (Brain does not run the solver locally).
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
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<AreaQueryBatchData>()) return;

            ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();
            if (batch.Count == 0) return;

            var ddsRequests = new List<DdsAreaQueryRequest>(batch.Count);
            for (int i = 0; i < batch.Count; i++)
            {
                var req = batch.Requests[i];

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

            // Brain does not run AreaQuerySolverSystem; clear queue after publishing.
            batch.Count = 0;
        }

        /// <inheritdoc/>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }

    // ── Muscle-side ingress: receives requests from Brain, fills batch for solver ──

    /// <summary>
    /// Muscle ingress translator: receives <see cref="AreaQueryRequestBatch"/> from Brain
    /// nodes via DDS and populates the local <see cref="AreaQueryBatchData"/> so
    /// <c>AreaQuerySolverSystem</c> can resolve them.
    /// If the area polygon entity cannot be resolved via <see cref="NetworkEntityMap"/>,
    /// writes an immediate ready result with <c>TargetCount = 0</c>.
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
            if (view is not EntityRepository repo) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;
                ProcessBatch(data, repo);
            }
        }

        /// <summary>
        /// Processes a single <see cref="AreaQueryRequestBatch"/> sample.
        /// Exposed as <c>internal</c> for unit test injection.
        /// </summary>
        internal void ProcessBatch(in AreaQueryRequestBatch batch, EntityRepository repo)
        {
            if (batch.Requests == null || batch.Requests.Count == 0) return;
            if (!repo.HasSingleton<AreaQueryBatchData>()) return;

            ref var localBatch = ref repo.GetSingleton<AreaQueryBatchData>();

            foreach (var ddsReq in batch.Requests)
            {
                if (localBatch.Count >= AreaQueryBatchData.DefaultCapacity) break;

                int slot = localBatch.Count;

                // Resolve the area polygon entity on this Muscle node.
                if (!_entityMap.TryGetEntity(ddsReq.TargetAreaNetworkId, out var areaEntity))
                {
                    // Area entity not known on this Muscle — emit an immediate empty result.
                    localBatch.Results[slot] = new AreaQueryResult
                    {
                        RequestId         = ddsReq.RequestId,
                        TargetCount       = 0,
                        TargetGroupHandle = -1,
                        SourceNodeId      = ddsReq.SourceNodeId,
                        IsReady           = true,
                    };
                    localBatch.Count = slot + 1;
                    continue;
                }

                localBatch.Requests[slot] = new AreaQueryRequest
                {
                    RequestId        = ddsReq.RequestId,
                    TargetAreaEntity = areaEntity,
                    TargetForce      = (ForceId)ddsReq.ForceId,
                    SourceNodeId     = ddsReq.SourceNodeId,
                };
                // Pre-initialize result slot so the solver writes IsReady=true correctly.
                localBatch.Results[slot] = new AreaQueryResult
                {
                    RequestId         = ddsReq.RequestId,
                    IsReady           = false,
                    TargetCount       = 0,
                    TargetGroupHandle = -1,
                    SourceNodeId      = ddsReq.SourceNodeId,
                };
                localBatch.Count = slot + 1;
            }
        }

        /// <inheritdoc/>
        public void ScanAndPublish(ISimulationView view) { }

        /// <inheritdoc/>
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        /// <inheritdoc/>
        public void Dispose(long networkEntityId) { }
    }

    // ── Muscle-side egress: reads solved results, sends responses to Brain ────────

    /// <summary>
    /// Muscle egress translator: reads resolved <see cref="AreaQueryResult"/>s from the
    /// local <see cref="AreaQueryBatchData"/> singleton, extracts entity handles from
    /// <see cref="EqsTargetPool"/>, resolves them to network IDs, and publishes
    /// <see cref="AreaQueryResponseBatch"/> messages grouped by <c>SourceNodeId</c>.
    /// Clears <c>batch.Count</c> after publishing.
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
            if (!repo.HasSingleton<AreaQueryBatchData>()) return;
            if (!repo.HasSingleton<EqsTargetPool>()) return;

            ref var batch = ref repo.GetSingleton<AreaQueryBatchData>();
            if (batch.Count == 0) return;

            var pool = repo.GetSingleton<EqsTargetPool>();

            // Group ready results by originating Brain node.
            var responsesByNode = new Dictionary<int, List<DdsAreaQueryResponse>>();
            bool anyReady = false;

            for (int i = 0; i < batch.Count; i++)
            {
                var result = batch.Results[i];
                if (!result.IsReady) continue;
                anyReady = true;

                var response = BuildResponse(result, in pool);

                if (!responsesByNode.TryGetValue(result.SourceNodeId, out var list))
                {
                    list = new List<DdsAreaQueryResponse>();
                    responsesByNode[result.SourceNodeId] = list;
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

            if (anyReady)
            {
                // Muscle is done with this batch; reset for the next ingress cycle.
                batch.Count = 0;
            }
        }

        private DdsAreaQueryResponse BuildResponse(in AreaQueryResult result, in EqsTargetPool pool)
        {
            var networkIds = new List<long>(result.TargetCount);

            if (result.TargetGroupHandle >= 0)
            {
                for (int t = 0; t < result.TargetCount; t++)
                {
                    int poolIdx = result.TargetGroupHandle + t;
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
                RequestId        = result.RequestId,
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

    // ── Brain-side ingress: receives responses from Muscle, fills local results ───

    /// <summary>
    /// Brain ingress translator: receives <see cref="AreaQueryResponseBatch"/> from the
    /// Muscle node via DDS, resolves network IDs back to local <see cref="Entity"/>
    /// handles, writes them into the Brain's <see cref="EqsTargetPool"/>, and sets
    /// <c>batch.Results[slot].IsReady = true</c> for the requesting BTree node.
    /// The result slot is decoded directly from the lower 32 bits of <c>RequestId</c>
    /// (which encodes the batch slot at submission time).
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
                // Decode the batch slot from the lower 32 bits of the RequestId.
                int slot = (int)(response.RequestId & 0xFFFFFFFF);
                if (slot < 0 || slot >= AreaQueryBatchData.DefaultCapacity) continue;

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
