using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Trajectory;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Hrot.NED.Common;
using Hrot.NED.Descriptors;

namespace Hrot.Network.NED.SimHost
{
    // ── Brain-side pathfinding translators (Brain -> NavigationSolver) ─────────────

    /// <summary>
    /// Egress translator. Reads <see cref="PathfindingRequestEvent"/>s from the previous
    /// frame's event buffer, converts absolute Cartesian start/end positions to relative
    /// ENU offsets relative to a GeoPoint anchor, and publishes a <c>PathRequestBatch</c>
    /// to the Navigation Solver via DDS.
    /// Only forwards requests whose <c>SourceNodeId</c> matches <c>_localNodeId</c>.
    /// </summary>
    public sealed class PathRequestBrainEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<PathRequestBatch>? _writer;
        private readonly IGeographicTransform _geoTransform;
        private readonly int _localNodeId;

        public long   DescriptorOrdinal => (long)EDescriptorType.dtPathRequestBatch;
        public string TopicName         => "PathRequestBatch";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public PathRequestBrainEgressTranslator(
            DdsParticipant?      participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform,
            int                  localNodeId = 0)
        {
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
            _localNodeId  = localNodeId;
            _writer       = participant != null ? new DdsWriter<PathRequestBatch>(participant, TopicName) : null;
        }

        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;

            // Read accumulated request events from the previous frame's READ buffer.
            var requests = view.ReadEvents<PathfindingRequestEvent>();
            if (requests.IsEmpty) return;

            var batch = BuildBatch(requests);
            if (batch == null) return;
            _writer.Write(batch.Value);
            SentSampleCount++;
        }

        /// <summary>The batch this node publishes for <paramref name="requests"/> (its own only), or null when none are
        /// its own. Exposed for unit tests.</summary>
        internal PathRequestBatch? BuildBatch(ReadOnlySpan<PathfindingRequestEvent> requests)
        {
            if (requests.IsEmpty) return null;

            // Use the first request's Start as the spatial anchor for encoding relative offsets.
            var anchorCartesian = requests[0].Start;
            var (lat, lon, alt) = _geoTransform.ToGeodetic(anchorCartesian);
            var batchOrigin = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt };

            var ddsRequests = new List<DdsPathRequest>(requests.Length);
            for (int i = 0; i < requests.Length; i++)
            {
                ref readonly var req = ref requests[i];

                // Authority filter: only forward requests originating from this node.
                if (req.SourceNodeId != _localNodeId) continue;

                ddsRequests.Add(new DdsPathRequest
                {
                    RequestId       = req.RequestId,
                    MobilityProfile = req.MobilityProfile,
                    // ⭐ CE-3129 — the order rides the wire to a solver on another node (the in-process path had it already).
                    BackendForce    = (byte)req.BackendForce,
                    NavLayerMask    = req.NavLayerMask,
                    RoadUse         = (byte)req.RoadUse,
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

            if (ddsRequests.Count == 0) return null;

            return new PathRequestBatch
            {
                SourceNodeId = _localNodeId,
                BatchOrigin  = batchOrigin,
                Requests     = ddsRequests,
            };
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Ingress translator on the Brain node. Receives <c>PathResponseBatch</c> from the
    /// Navigation Solver, reconstructs absolute Cartesian waypoints from the BatchOrigin
    /// + relative ENU offsets, registers a new local trajectory in the Brain's
    /// <c>TrajectoryPoolManager</c>, and writes the results into
    /// <c>PathfindingBatchData.Results</c> directly (runs on the main thread).
    /// The ring-buffer slot is derived from <c>requestId % DefaultCapacity</c>.
    /// </summary>
    public sealed class PathResponseBrainIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<PathResponseBatch>? _reader;
        private readonly IGeographicTransform _geoTransform;
        private readonly TrajectoryPoolManager _trajectoryPool;
        private readonly int _localNodeId;

        public long   DescriptorOrdinal => (long)EDescriptorType.dtPathResponseBatch;
        public string TopicName         => "PathResponseBatch";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public PathResponseBrainIngressTranslator(
            DdsParticipant?       participant,
            NetworkEntityMap      entityMap,
            IGeographicTransform  geoTransform,
            TrajectoryPoolManager trajectoryPool,
            int                   localNodeId = 0)
        {
            _geoTransform   = geoTransform   ?? throw new ArgumentNullException(nameof(geoTransform));
            _trajectoryPool = trajectoryPool ?? throw new ArgumentNullException(nameof(trajectoryPool));
            _localNodeId    = localNodeId;
            _reader         = participant != null ? new DdsReader<PathResponseBatch>(participant, TopicName) : null;
        }

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

                // Network routing firewall: only accept responses addressed to this node or broadcast.
                if (data.TargetNodeId != _localNodeId && data.TargetNodeId != 0) continue;

                ProcessBatch(data, repo);
            }
        }

        /// <summary>
        /// Processes a single <see cref="PathResponseBatch"/> sample.
        /// Exposed as <c>internal</c> for unit test injection.
        /// Runs on the main thread — direct writes to NativeArray and struct singletons are safe.
        /// </summary>
        internal void ProcessBatch(in PathResponseBatch data, EntityRepository repo)
        {
            if (data.Results == null || data.Results.Count == 0) return;
            if (!repo.HasSingleton<PathfindingBatchData>()) return;

            ref var batch = ref repo.GetSingleton<PathfindingBatchData>();

            // Spatial precision reconstruction: convert geodetic anchor to absolute Cartesian.
            var originCartesian = _geoTransform.ToCartesian(
                data.BatchOrigin.Latitude,
                data.BatchOrigin.Longitude,
                data.BatchOrigin.Altitude);
            // 3D anchor (Sim Z-up): ToCartesian carries the anchor altitude (P3D-304).
            var anchor3D = new Vector3((float)originCartesian.X, (float)originCartesian.Y, (float)originCartesian.Z);

            foreach (var ddsResult in data.Results)
            {
                int slot = (int)((uint)ddsResult.RequestId % (uint)PathfindingBatchData.DefaultCapacity);

                int localRouteHandle = -1;

                if (ddsResult.IsReachable && ddsResult.CoarseWaypoints != null && ddsResult.CoarseWaypoints.Count >= 2)
                {
                    // Reconstruct absolute 3D waypoints from the relative ENU offsets (Up carried).
                    var positions = new Vector3[ddsResult.CoarseWaypoints.Count];
                    for (int w = 0; w < ddsResult.CoarseWaypoints.Count; w++)
                    {
                        var rel = ddsResult.CoarseWaypoints[w];
                        positions[w] = anchor3D + new Vector3(rel.East, rel.North, rel.Up);
                    }

                    // Register in the local pool; get a new local handle.
                    localRouteHandle = _trajectoryPool.RegisterTrajectory(positions);
                }

                batch.Results[slot] = new PathResult
                {
                    RequestId           = ddsResult.RequestId,
                    IsReachable         = ddsResult.IsReachable,
                    TotalDistanceMeters = ddsResult.TotalDistanceMeters,
                    RouteHandle         = localRouteHandle,
                    SourceNodeId        = _localNodeId,
                };
            }
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    // ── Solver-side pathfinding translators (NavigationSolver -> Brain) ────────────

    /// <summary>
    /// Ingress translator on the Navigation Solver. Receives <c>PathRequestBatch</c> from Brain
    /// nodes, reconstructs absolute Cartesian start/end from the BatchOrigin anchor, and
    /// publishes <see cref="PathfindingRequestEvent"/>s via <see cref="IEntityCommandBuffer"/>
    /// so <c>PathfindingSolverSystem</c> can resolve them on its next background tick.
    /// Stamps <c>SourceNodeId</c> for return-path demultiplexing.
    /// </summary>
    public sealed class PathRequestSolverIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<PathRequestBatch>? _reader;
        private readonly IGeographicTransform _geoTransform;

        public long   DescriptorOrdinal => (long)EDescriptorType.dtPathRequestBatch;
        public string TopicName         => "PathRequestBatch";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public PathRequestSolverIngressTranslator(
            DdsParticipant?      participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform)
        {
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
            _reader       = participant != null ? new DdsReader<PathRequestBatch>(participant, TopicName) : null;
        }

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
        /// Processes a single <see cref="PathRequestBatch"/> sample.
        /// Exposed as <c>internal</c> for unit test injection.
        /// </summary>
        internal void ProcessBatch(in PathRequestBatch data, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (data.Requests == null || data.Requests.Count == 0) return;

            // Spatial precision reconstruction: convert geodetic anchor to absolute Cartesian.
            var originCartesian = _geoTransform.ToCartesian(
                data.BatchOrigin.Latitude,
                data.BatchOrigin.Longitude,
                data.BatchOrigin.Altitude);
            var anchor = new Vector3(
                (float)originCartesian.X,
                (float)originCartesian.Y,
                (float)originCartesian.Z);

            foreach (var ddsReq in data.Requests)
            {
                var start = anchor + new Vector3(ddsReq.Start.East, ddsReq.Start.North, ddsReq.Start.Up);
                var end   = anchor + new Vector3(ddsReq.End.East,   ddsReq.End.North,   ddsReq.End.Up);

                // Queue the request for the background solver.
                cmd.PublishEvent(new PathfindingRequestEvent
                {
                    RequestId       = ddsReq.RequestId,
                    Start           = start,
                    End             = end,
                    MobilityProfile = ddsReq.MobilityProfile,
                    BackendForce    = (NavigationBackend)ddsReq.BackendForce,   // ⭐ CE-3129
                    NavLayerMask    = ddsReq.NavLayerMask,
                    RoadUse         = (RoadUse)ddsReq.RoadUse,
                    // Stamp the originating Brain node ID for demultiplexing on egress.
                    SourceNodeId    = data.SourceNodeId,
                });
            }
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Egress translator on the Navigation Solver. Reads resolved
    /// <see cref="PathfindingResultEvent"/>s from the <see cref="FdpEventBus"/>,
    /// extracts waypoints from the solver's local <c>TrajectoryPoolManager</c>,
    /// converts positions to relative ENU offsets from a GeoPoint anchor,
    /// groups results by originating Brain node, and publishes targeted
    /// <c>PathResponseBatch</c> messages via DDS.
    /// </summary>
    public sealed class PathResponseSolverEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<PathResponseBatch>? _writer;
        private readonly IGeographicTransform _geoTransform;
        private readonly TrajectoryPoolManager _trajectoryPool;

        public long   DescriptorOrdinal => (long)EDescriptorType.dtPathResponseBatch;
        public string TopicName         => "PathResponseBatch";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount     { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public PathResponseSolverEgressTranslator(
            DdsParticipant?       participant,
            NetworkEntityMap      entityMap,
            IGeographicTransform  geoTransform,
            TrajectoryPoolManager trajectoryPool)
        {
            _geoTransform   = geoTransform   ?? throw new ArgumentNullException(nameof(geoTransform));
            _trajectoryPool = trajectoryPool ?? throw new ArgumentNullException(nameof(trajectoryPool));
            _writer         = participant != null ? new DdsWriter<PathResponseBatch>(participant, TopicName) : null;
        }

        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;

            // Read resolved result events from the previous frame's READ buffer.
            var results = view.ReadEvents<PathfindingResultEvent>();
            if (results.IsEmpty) return;

            foreach (var batch in BuildBatches(results))
            {
                _writer.Write(batch);
                SentSampleCount++;
            }
        }

        /// <summary>One node's batch while it is being built: every result in it is encoded against ONE anchor.</summary>
        private sealed class NodeBatch
        {
            public readonly List<DdsPathResult> Results = new();
            public bool HasAnchor;
            public Vector3 Anchor;
            public GeoPoint Origin;
        }

        /// <summary>
        /// Groups <paramref name="results"/> by originating Brain node, one <see cref="PathResponseBatch"/> per node.
        /// ⭐ CE-3129 — every result in a batch is relative to the batch's ONE <c>BatchOrigin</c> (the first reachable
        /// result's first waypoint), which is how the Brain decodes it. ⛔ Each result used to be relative to its OWN
        /// first waypoint while the batch carried the LAST reachable result's, so with two or more results for one node
        /// every route but the last landed displaced on the Brain. Exposed for unit tests.
        /// </summary>
        internal List<PathResponseBatch> BuildBatches(ReadOnlySpan<PathfindingResultEvent> results)
        {
            var byNode = new Dictionary<int, NodeBatch>();
            var order = new List<int>();
            for (int i = 0; i < results.Length; i++)
            {
                ref readonly var evt = ref results[i];
                if (!byNode.TryGetValue(evt.SourceNodeId, out var nb))
                {
                    nb = new NodeBatch();
                    byNode[evt.SourceNodeId] = nb;
                    order.Add(evt.SourceNodeId);
                }

                List<RelativeVector3>? coarseWaypoints = null;
                if (evt.IsReachable && evt.RouteHandle >= 0
                    && _trajectoryPool.TryGetTrajectory(evt.RouteHandle, out var traj) && traj.Waypoints.Length > 0)
                {
                    // The batch anchor is the first reachable result's first waypoint. Positions are 3D (Sim Z-up); the
                    // anchor and per-waypoint Up carry real altitude rather than flattening to 0 (P3D-304).
                    if (!nb.HasAnchor)
                    {
                        nb.Anchor = traj.Waypoints[0].Position;
                        var (lat, lon, alt) = _geoTransform.ToGeodetic(nb.Anchor);
                        nb.Origin = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt };
                        nb.HasAnchor = true;
                    }

                    coarseWaypoints = new List<RelativeVector3>(traj.Waypoints.Length);
                    for (int w = 0; w < traj.Waypoints.Length; w++)
                    {
                        var wp = traj.Waypoints[w].Position;
                        coarseWaypoints.Add(new RelativeVector3
                        {
                            East  = wp.X - nb.Anchor.X,
                            North = wp.Y - nb.Anchor.Y,
                            Up    = wp.Z - nb.Anchor.Z,
                        });
                    }
                }

                nb.Results.Add(new DdsPathResult
                {
                    RequestId           = evt.RequestId,
                    IsReachable         = evt.IsReachable,
                    TotalDistanceMeters = evt.TotalDistanceMeters,
                    RouteHandle         = evt.RouteHandle,
                    CoarseWaypoints     = coarseWaypoints!,
                });
            }

            var batches = new List<PathResponseBatch>(order.Count);
            foreach (int node in order)
            {
                var nb = byNode[node];
                batches.Add(new PathResponseBatch { TargetNodeId = node, BatchOrigin = nb.Origin, Results = nb.Results });
            }
            return batches;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}

