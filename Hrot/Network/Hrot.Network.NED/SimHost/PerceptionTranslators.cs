using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Spatial;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Math;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Utilities;
using Fdp.ModuleHost.Abstractions;
using Hrot.NED.Common;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.SimHost
{
    // ── Brain-side perception translators (Brain -> Perception Solver) ────────────

    /// <summary>
    /// Egress translator. Publishes <c>SensorConfig</c> for owned entities (Brain -> Solver).
    /// Converts the FDP cosine-based FOV back to wire-format degrees, applies SmartEgress
    /// dirty-tracking so configs are only re-sent when they change.
    /// </summary>
    public sealed class SensorConfigEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<SensorConfig>? _writer;

        public long   DescriptorOrdinal => 60;
        public string TopicName         => "SensorConfig";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public SensorConfigEgressTranslator(
            DdsParticipant?      participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform)   // geoTransform reserved for future geodetic encoding
        {
            _writer = participant != null ? new DdsWriter<SensorConfig>(participant, TopicName) : null;
        }

        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;

            var query = view.Query()
                .With<PerceptionReceptor>()
                .With<NetworkIdentity>()
                .Build();

            foreach (var entity in query)
            {
                // Authority gate: only publish for entities this node owns.
                if (!view.HasAuthority(entity, DescriptorOrdinal)) continue;

                // SmartEgress dirty-tracking: skip entities whose config hasn't changed.
                if (!SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal, isUnreliable: false))
                    continue;

                ref readonly var perc    = ref view.GetComponentRO<PerceptionReceptor>(entity);
                ref readonly var netId   = ref view.GetComponentRO<NetworkIdentity>(entity);

                // Convert precomputed cosine of half-angle back to full FOV degrees for the wire schema.
                float halfFovRad = MathF.Acos(Math.Clamp(perc.FieldOfViewCos, -1f, 1f));
                float fovDegrees = halfFovRad * 2f * (180f / MathF.PI);

                _writer.Write(new SensorConfig
                {
                    EntityId     = netId.Value,
                    VisionRange  = perc.VisionRange,
                    HearingRange = perc.HearingRange,
                    FovDegrees   = fovDegrees,
                });
                SentSampleCount++;

                SmartEgressUtil.MarkPublished(view, entity, DescriptorOrdinal);
            }
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId)
        {
            _writer?.DisposeInstance(new SensorConfig { EntityId = networkEntityId });
        }
    }

    /// <summary>
    /// Egress translator. Reads the local unmanaged <c>RaycastBatchData</c> singleton on
    /// the Brain node, converts absolute Cartesian vectors to relative ENU offsets, and
    /// publishes a <c>RaycastRequestBatch</c> to the Perception Solver.
    /// Clears the batch after publishing (Brain does not run HitResolutionSystem).
    /// </summary>
    public sealed class RaycastBatchEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<RaycastRequestBatch>? _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform _geoTransform;
        private readonly int _localNodeId;

        private uint _batchCorrelationId;

        public long   DescriptorOrdinal => 61;
        public string TopicName         => "RaycastRequestBatch";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public RaycastBatchEgressTranslator(
            DdsParticipant?      participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform,
            int                  localNodeId = 0)
        {
            _entityMap    = entityMap    ?? throw new ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
            _localNodeId  = localNodeId;
            _writer       = participant != null ? new DdsWriter<RaycastRequestBatch>(participant, TopicName) : null;
        }

        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<RaycastBatchData>()) return;

            ref var batch = ref repo.GetSingleton<RaycastBatchData>();
            if (batch.Count == 0) return;

            // Spatial precision anchor: convert first ray origin to WGS-84 GeoPoint.
            var anchorCartesian = batch.Requests[0].Start;
            var (lat, lon, alt) = _geoTransform.ToGeodetic(anchorCartesian);
            var batchOrigin = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt };

            var ddsRequests = new List<DdsRaycastRequest>(batch.Count);
            for (int i = 0; i < batch.Count; i++)
            {
                var req = batch.Requests[i];

                // Network firewall: map local ECS entity handle to network ID.
                long ignoreNetId = 0;
                if (!req.IgnoreEntity.IsNull)
                    _entityMap.TryGetNetworkId(req.IgnoreEntity, out ignoreNetId);

                ddsRequests.Add(new DdsRaycastRequest
                {
                    RayId          = req.RayId,
                    LayerMask      = req.LayerMask,
                    IgnoreEntityId = ignoreNetId,
                    Start = new RelativeVector3
                    {
                        East  = req.Start.X - anchorCartesian.X,
                        North = req.Start.Y - anchorCartesian.Y,
                        Up    = req.Start.Z - anchorCartesian.Z,
                    },
                    End = new RelativeVector3
                    {
                        East  = req.End.X - anchorCartesian.X,
                        North = req.End.Y - anchorCartesian.Y,
                        Up    = req.End.Z - anchorCartesian.Z,
                    },
                });
            }

            _writer.Write(new RaycastRequestBatch
            {
                SourceNodeId       = _localNodeId,
                BatchCorrelationId = ++_batchCorrelationId,
                BatchOrigin        = batchOrigin,
                Requests           = ddsRequests,
            });
            SentSampleCount++;

            // Brain does not run HitResolutionSystem; clear queue after publishing.
            batch.Count = 0;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Ingress translator. Receives discrete <c>SensorTrackState</c> events from the Perception Solver
    /// and maintains the local <c>ActiveSensorTracks</c> cognitive buffer on Brain-side observer entities.
    /// <list type="bullet">
    ///   <item>State = 1 (Acquired): adds or updates the target in <see cref="ActiveSensorTracks"/>.</item>
    ///   <item>State = 0 (Lost): compact-removes the target slot (swap-with-last, decrement Count).</item>
    /// </list>
    /// The <see cref="Fdp.Toolkit.Perception.Systems.ThreatEvaluationSystem"/> on the CGF node
    /// continuously boosts and decays <c>TargetMemory</c> based on this buffer every frame.
    /// Enforces entity-pointer safety: raw network IDs are resolved to generational ECS handles before storage.
    /// </summary>
    public sealed class SensorTrackStateIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<SensorTrackState>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public long   DescriptorOrdinal => 62;
        public string TopicName         => "SensorTrackState";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public SensorTrackStateIngressTranslator(
            DdsParticipant?  participant,
            NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant != null ? new DdsReader<SensorTrackState>(participant, TopicName) : null;
        }

        public unsafe void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;

                // Resolve observer network ID to local ECS handle.
                if (!_entityMap.TryGetEntity(data.ObserverEntityId, out var observer)) continue;
                if (!view.IsAlive(observer)) continue;

                // Resolve target network ID to local ECS handle.
                if (!_entityMap.TryGetEntity(data.TargetEntityId, out var targetEntity)) continue;

                long localTargetId = (long)targetEntity.PackedValue;

                // Bootstrap or read existing ActiveSensorTracks.
                bool hasComponent = view.HasComponent<ActiveSensorTracks>(observer);
                ActiveSensorTracks tracks = hasComponent
                    ? view.GetComponentRO<ActiveSensorTracks>(observer)
                    : new ActiveSensorTracks();

                if (data.State == 1) // Acquired
                {
                    // Update position if already tracked, or add a new slot.
                    bool found = false;
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        if (tracks.EntityIds[i] == localTargetId)
                        {
                            tracks.PositionsX[i] = data.PositionX;
                            tracks.PositionsY[i] = data.PositionY;
                            found = true;
                            break;
                        }
                    }
                    if (!found && tracks.Count < PerceptionConstants.MaxTrackedTargets)
                    {
                        tracks.EntityIds[tracks.Count]  = localTargetId;
                        tracks.PositionsX[tracks.Count] = data.PositionX;
                        tracks.PositionsY[tracks.Count] = data.PositionY;
                        tracks.Count++;
                    }
                }
                else // Lost (State == 0)
                {
                    // Compact-remove: swap the target slot with the last entry, then shrink Count.
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        if (tracks.EntityIds[i] != localTargetId) continue;
                        int last = tracks.Count - 1;
                        if (i < last)
                        {
                            tracks.EntityIds[i]  = tracks.EntityIds[last];
                            tracks.PositionsX[i] = tracks.PositionsX[last];
                            tracks.PositionsY[i] = tracks.PositionsY[last];
                        }
                        tracks.Count--;
                        break;
                    }
                }

                if (hasComponent)
                    cmd.SetComponent(observer, tracks);
                else
                    cmd.AddComponent(observer, tracks);
            }
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Ingress translator. Receives <c>RaycastResponseBatch</c> from the Perception Solver
    /// and injects the resolved hit results into the local <c>RaycastBatchData</c> singleton.
    /// Accepts TargetNodeId == 0 (broadcast) in addition to the local node ID.
    /// </summary>
    public sealed class RaycastBatchIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<RaycastResponseBatch>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly int _localNodeId;

        public long   DescriptorOrdinal => 63;
        public string TopicName         => "RaycastResponseBatch";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public RaycastBatchIngressTranslator(
            DdsParticipant?  participant,
            NetworkEntityMap entityMap,
            int              localNodeId = 0)
        {
            _entityMap   = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = localNodeId;
            _reader      = participant != null ? new DdsReader<RaycastResponseBatch>(participant, TopicName) : null;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<RaycastBatchData>()) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;

                // Network routing firewall: only process responses addressed to this node or broadcast (0).
                if (data.TargetNodeId != _localNodeId && data.TargetNodeId != 0) continue;
                if (data.Hits == null || data.Hits.Count == 0) continue;

                ref var batch = ref repo.GetSingleton<RaycastBatchData>();

                foreach (var ddsHit in data.Hits)
                {
                    if (batch.Count >= PhysicsConstants.RaycastBatchCapacity) break;

                    // Memory index firewall: map network IDs back to generational ECS handles.
                    Entity hitEntity = Entity.Null;
                    if (ddsHit.HasHit && ddsHit.HitEntityId != 0)
                        _entityMap.TryGetEntity(ddsHit.HitEntityId, out hitEntity);

                    int idx = batch.Count;

                    batch.Hits[idx] = new RaycastHit
                    {
                        RayId     = ddsHit.RayId,
                        HasHit    = (byte)(ddsHit.HasHit ? 1 : 0),
                        HitEntity = hitEntity,
                        T         = ddsHit.HitT,
                    };

                    // Zero out the parallel request slot to prevent the egress translator
                    // from re-transmitting stale requests on the next frame.
                    batch.Requests[idx] = default;

                    batch.Count++;
                }
            }
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    // ── Solver-side perception translators (Perception Solver -> Brain) ────────────

    /// <summary>
    /// Ingress translator. Receives <c>SensorConfig</c> from the Brain node and applies it
    /// to the local <c>PerceptionReceptor</c> component on the
    /// Perception Solver.  Performs the FovDegrees -> FovCos conversion at the network boundary
    /// so the spatial-hash solver never executes trigonometry on the hot path.
    /// </summary>
    public sealed class SensorConfigIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<SensorConfig>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;

        public long   DescriptorOrdinal => 60;
        public string TopicName         => "SensorConfig";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public SensorConfigIngressTranslator(
            DdsParticipant?     participant,
            NetworkEntityMap    entityMap,
            GhostCreationSystem ghostCreationSystem)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _reader    = participant != null ? new DdsReader<SensorConfig>(participant, TopicName) : null;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;

                if (!_entityMap.TryGetEntity(data.EntityId, out var entity))
                {
                    var repo = view as EntityRepository;
                    if (repo == null) continue;
                    entity = _ghostCreationSystem.CreateGhost(repo, data.EntityId, view.Tick);
                }

                // ACL transformation: precompute FovCos once at the network boundary.
                float halfFovRad = data.FovDegrees * 0.5f * (MathF.PI / 180f);
                float fovCos = MathF.Cos(halfFovRad);

                cmd.SetComponent(entity, new PerceptionReceptor
                {
                    VisionRange    = data.VisionRange,
                    HearingRange   = data.HearingRange,
                    FieldOfViewCos = fovCos,
                });

            }
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Ingress translator. Receives <c>RaycastRequestBatch</c> from the Brain node,
    /// resolves rays immediately against local colliders, and publishes
    /// <c>RaycastResponseBatch</c> back to the requesting brain node.
    /// This avoids touching the shared <c>RaycastBatchData</c> singleton used by ballistics.
    /// </summary>
    public sealed class RaycastBatchSolverIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<RaycastRequestBatch>? _reader;
        private readonly DdsWriter<RaycastResponseBatch>? _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly IGeographicTransform _geoTransform;

        public long   DescriptorOrdinal => 61;
        public string TopicName         => "RaycastRequestBatch";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public RaycastBatchSolverIngressTranslator(
            DdsParticipant?      participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform)
        {
            _entityMap    = entityMap    ?? throw new ArgumentNullException(nameof(entityMap));
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
            _reader       = participant != null ? new DdsReader<RaycastRequestBatch>(participant, TopicName) : null;
            _writer       = participant != null ? new DdsWriter<RaycastResponseBatch>(participant, "RaycastResponseBatch") : null;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null || _writer is null) return;
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<SpatialGridData>()) return;
            Span<(Entity entity, Vector2 pos)> candidates = stackalloc (Entity, Vector2)[PhysicsConstants.MaxBroadphaseCandidates];

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;
                if (data.Requests == null || data.Requests.Count == 0) continue;

                // Spatial precision reconstruction: convert geodetic anchor to absolute Cartesian.
                var originCartesian = _geoTransform.ToCartesian(
                    data.BatchOrigin.Latitude,
                    data.BatchOrigin.Longitude,
                    data.BatchOrigin.Altitude);
                var anchor = new Vector3(
                    (float)originCartesian.X,
                    (float)originCartesian.Y,
                    (float)originCartesian.Z);

                var grid = repo.GetSingleton<SpatialGridData>().Grid;
                var responseHits = new List<DdsRaycastHit>(data.Requests.Count);

                foreach (var ddsReq in data.Requests)
                {
                    var start = anchor + new Vector3(ddsReq.Start.East, ddsReq.Start.North, ddsReq.Start.Up);
                    var end = anchor + new Vector3(ddsReq.End.East, ddsReq.End.North, ddsReq.End.Up);

                    var start2D = new Vector2(start.X, start.Y);
                    var end2D = new Vector2(end.X, end.Y);
                    var midpoint = (start2D + end2D) * 0.5f;
                    float queryRadius = Vector2.Distance(start2D, end2D) * 0.5f + PhysicsConstants.QueryExpansionRadius;

                    int candidateCount = grid.QueryNeighbors(midpoint, queryRadius, candidates);

                    float bestT = float.MaxValue;
                    long hitNetId = 0;
                    bool anyHit = false;
                    long ignoreNetId = ddsReq.IgnoreEntityId;

                    for (int i = 0; i < candidateCount; i++)
                    {
                        Entity candidate = candidates[i].entity;
                        if (!repo.IsAlive(candidate) || !repo.HasComponent<PhysicsCollider>(candidate)) continue;

                        if (ignoreNetId != 0 &&
                            _entityMap.TryGetNetworkId(candidate, out long candidateNetId) &&
                            candidateNetId == ignoreNetId)
                            continue;

                        var collider = repo.GetComponentRO<PhysicsCollider>(candidate);
                        if ((ddsReq.LayerMask & collider.CollisionLayer) == 0) continue;

                        if (Intersection2D.RaycastCircle(start2D, end2D, candidates[i].pos, collider.Radius, out float t) && t < bestT)
                        {
                            bestT = t;
                            anyHit = true;
                            _entityMap.TryGetNetworkId(candidate, out hitNetId);
                        }
                    }

                    responseHits.Add(new DdsRaycastHit
                    {
                        RayId = ddsReq.RayId,
                        HasHit = anyHit,
                        HitEntityId = hitNetId,
                        HitT = anyHit ? bestT : 0f,
                    });
                }

                _writer.Write(new RaycastResponseBatch
                {
                    TargetNodeId = data.SourceNodeId,
                    BatchCorrelationId = data.BatchCorrelationId,
                    Hits = responseHits,
                });
                SentSampleCount++;
            }
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Egress translator. Monitors <c>SensorContactList</c> on the Perception Solver each tick and
    /// emits discrete <c>SensorTrackState</c> samples to Brain nodes only when a target transitions
    /// to <see cref="SensorContactState.Acquired"/> or falls back to
    /// <see cref="SensorContactState.Lost"/>.  The previous-tick acquired set for each observer is
    /// maintained in <see cref="_previousTargets"/>.
    /// Uses Reliable / TransientLocal QoS -- one sample per contact event,
    /// never a continuous per-tick flood.
    /// </summary>
    public sealed class SensorTrackStateEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<SensorTrackState>? _writer;
        private readonly NetworkEntityMap _entityMap;

        // Per observer (keyed by observer network ID): set of target network IDs currently acquired.
        private readonly Dictionary<long, HashSet<long>> _previousTargets = new();

        // Scratch set reused each tick to detect stale observer entries without allocation.
        private readonly HashSet<long> _seenThisTick = new();

        public long   DescriptorOrdinal => 62;
        public string TopicName         => "SensorTrackState";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public SensorTrackStateEgressTranslator(
            DdsParticipant?  participant,
            NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _writer    = participant != null ? new DdsWriter<SensorTrackState>(participant, TopicName) : null;
        }

        public unsafe void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;

            // Read the physical sensor contact lists instead of cognitive TargetMemory.
            var query = view.Query()
                .With<SensorContactList>()
                .With<NetworkIdentity>()
                .Build();

            _seenThisTick.Clear();

            foreach (var entity in query)
            {
                ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
                ref readonly var list  = ref view.GetComponentRO<SensorContactList>(entity);

                long observerId = netId.Value;
                _seenThisTick.Add(observerId);

                if (!_previousTargets.TryGetValue(observerId, out var previous))
                {
                    previous = new HashSet<long>();
                    _previousTargets[observerId] = previous;
                }

                // Build current set of actively Acquired targets (network IDs).
                var current = new HashSet<long>(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    if (list.State[i] != (byte)SensorContactState.Acquired) continue;
                    var localTarget = new Entity((ulong)list.EntityIds[i]);
                    if (_entityMap.TryGetNetworkId(localTarget, out long targetNetId))
                        current.Add(targetNetId);
                }

                // Emit Acquired events for newly tracked targets.
                foreach (long targetNetId in current)
                {
                    if (previous.Contains(targetNetId)) continue;

                    // Resolve physical position for the Acquired packet.
                    float posX = 0f, posY = 0f;
                    if (_entityMap.TryGetEntity(targetNetId, out var localTarget) &&
                        view.IsAlive(localTarget) &&
                        view.HasComponent<SimTransform>(localTarget))
                    {
                        ref readonly var targetTf = ref view.GetComponentRO<SimTransform>(localTarget);
                        posX = targetTf.Position.X;
                        posY = targetTf.Position.Y;
                    }

                    _writer.Write(new SensorTrackState
                    {
                        ObserverEntityId = observerId,
                        TargetEntityId   = targetNetId,
                        State            = 1, // Acquired
                        PositionX        = posX,
                        PositionY        = posY,
                        Tick             = view.Tick,
                    });
                    SentSampleCount++;
                }

                // Emit Lost events for targets that dropped out of Acquired state.
                foreach (long targetNetId in previous)
                {
                    if (current.Contains(targetNetId)) continue;

                    // Spatial data is irrelevant for a Lost packet.
                    _writer.Write(new SensorTrackState
                    {
                        ObserverEntityId = observerId,
                        TargetEntityId   = targetNetId,
                        State            = 0, // Lost
                        PositionX        = 0f,
                        PositionY        = 0f,
                        Tick             = view.Tick,
                    });
                    SentSampleCount++;
                }

                // Update tracking state for this observer.
                _previousTargets[observerId] = current;
            }

            // Remove state for observers that have left the query (entity destroyed or SensorContactList removed).
            var staleKeys = new List<long>();
            foreach (var key in _previousTargets.Keys)
            {
                if (!_seenThisTick.Contains(key))
                    staleKeys.Add(key);
            }
            foreach (long staleId in staleKeys)
                _previousTargets.Remove(staleId);
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId)
        {
            _previousTargets.Remove(networkEntityId);
        }
    }

    /// <summary>
    /// Egress translator. Reads the completed raycast hits from <c>RaycastBatchData</c> on the
    /// Perception Solver, filters out bullet rays (those stay local to SimHost), groups the
    /// remaining LOS results by originating Brain node, and publishes targeted
    /// <c>RaycastResponseBatch</c> messages.
    /// Acts as the terminal sink for the Solver's physics pipeline (clears batch.Count).
    /// </summary>
    public sealed class RaycastBatchSolverEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<RaycastResponseBatch>? _writer;
        private readonly NetworkEntityMap _entityMap;

        private uint _batchCorrelationId;

        public long   DescriptorOrdinal => 63;
        public string TopicName         => "RaycastResponseBatch";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public RaycastBatchSolverEgressTranslator(
            DdsParticipant?  participant,
            NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _writer    = participant != null ? new DdsWriter<RaycastResponseBatch>(participant, TopicName) : null;
        }

        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<RaycastBatchData>()) return;

            ref var batch = ref repo.GetSingleton<RaycastBatchData>();
            if (batch.Count == 0) return;

            // Demultiplex LOS hits by originating Brain node.
            // Bullet rays stay local -- filtered out here.
            var batchesByNode = new Dictionary<int, List<DdsRaycastHit>>();

            for (int i = 0; i < batch.Count; i++)
            {
                var hit = batch.Hits[i];

                // Do not route bullet rays back to Brain -- damage stays on SimHost.
                if (PhysicsConstants.IsBulletRay(hit.RayId)) continue;

                if (!batchesByNode.TryGetValue(hit.SourceNodeId, out var hitList))
                {
                    hitList = new List<DdsRaycastHit>();
                    batchesByNode[hit.SourceNodeId] = hitList;
                }

                // Firewall: map local ECS hit handle to network ID.
                long hitNetId = 0;
                if (hit.HasHit != 0 && !hit.HitEntity.IsNull)
                    _entityMap.TryGetNetworkId(hit.HitEntity, out hitNetId);

                hitList.Add(new DdsRaycastHit
                {
                    RayId       = hit.RayId,
                    HasHit      = hit.HasHit != 0,
                    HitEntityId = hitNetId,
                    HitT        = hit.T,
                });
            }

            // Publish one targeted batch per originating Brain node.
            foreach (var kvp in batchesByNode)
            {
                _writer.Write(new RaycastResponseBatch
                {
                    TargetNodeId       = kvp.Key,
                    BatchCorrelationId = ++_batchCorrelationId,
                    Hits               = kvp.Value,
                });
                SentSampleCount++;
            }

            // Terminal sink: Solver does not run HitResolutionSystem; flush the queue.
            batch.Count = 0;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
