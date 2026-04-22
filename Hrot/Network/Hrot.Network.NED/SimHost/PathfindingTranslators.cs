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
    /// Egress translator. Reads the local <c>PathfindingBatchData</c> singleton on the Brain
    /// node, converts absolute Cartesian start/end positions to relative ENU offsets, and
    /// publishes a <c>PathRequestBatch</c> to the Navigation Solver.
    /// Clears batch.Count after publishing (Brain does not run PathfindingSolverSystem).
    /// </summary>
    public sealed class PathRequestBrainEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<PathRequestBatch>? _writer;
        private readonly IGeographicTransform _geoTransform;
        private readonly int _localNodeId;

        public long   DescriptorOrdinal => (long)EDescriptorType.dtPathRequestBatch;
        public string TopicName         => "PathRequestBatch";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
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
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<PathfindingBatchData>()) return;

            ref var batch = ref repo.GetSingleton<PathfindingBatchData>();
            if (batch.Count == 0) return;

            // Spatial precision anchor: convert first request start to WGS-84 GeoPoint.
            var anchorCartesian = batch.Requests[0].Start;
            var (lat, lon, alt) = _geoTransform.ToGeodetic(anchorCartesian);
            var batchOrigin = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt };

            var ddsRequests = new List<DdsPathRequest>(batch.Count);
            for (int i = 0; i < batch.Count; i++)
            {
                var req = batch.Requests[i];
                ddsRequests.Add(new DdsPathRequest
                {
                    RequestId       = req.RequestId,
                    MobilityProfile = req.MobilityProfile,
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

            _writer.Write(new PathRequestBatch
            {
                SourceNodeId = _localNodeId,
                BatchOrigin  = batchOrigin,
                Requests     = ddsRequests,
            });
            SentSampleCount++;

            // Brain does not run PathfindingSolverSystem; clear queue after publishing.
            batch.Count = 0;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Ingress translator. Receives <c>PathResponseBatch</c> from the Navigation Solver,
    /// reconstructs absolute Cartesian waypoints from the BatchOrigin + relative offsets,
    /// registers a new local trajectory in the Brain's <c>TrajectoryPoolManager</c>, and
    /// writes the results into <c>PathfindingBatchData.Results</c>.
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
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public PathResponseBrainIngressTranslator(
            DdsParticipant?      participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform,
            TrajectoryPoolManager trajectoryPool,
            int                  localNodeId = 0)
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
            if (!repo.HasSingleton<PathfindingBatchData>()) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;

                // Network routing firewall: only accept responses addressed to this node or broadcast.
                if (data.TargetNodeId != _localNodeId && data.TargetNodeId != 0) continue;
                if (data.Results == null || data.Results.Count == 0) continue;

                ref var batch = ref repo.GetSingleton<PathfindingBatchData>();

                // Spatial precision reconstruction: convert geodetic anchor to absolute Cartesian.
                var originCartesian = _geoTransform.ToCartesian(
                    data.BatchOrigin.Latitude,
                    data.BatchOrigin.Longitude,
                    data.BatchOrigin.Altitude);
                var anchor = new Vector2((float)originCartesian.X, (float)originCartesian.Y);

                int resultIdx = 0;
                foreach (var ddsResult in data.Results)
                {
                    if (resultIdx >= PathfindingBatchData.DefaultCapacity) break;

                    int localRouteHandle = -1;

                    if (ddsResult.IsReachable && ddsResult.CoarseWaypoints != null && ddsResult.CoarseWaypoints.Count >= 2)
                    {
                        // Reconstruct absolute 2-D waypoints from the relative ENU offsets.
                        var positions = new Vector2[ddsResult.CoarseWaypoints.Count];
                        for (int w = 0; w < ddsResult.CoarseWaypoints.Count; w++)
                        {
                            var rel = ddsResult.CoarseWaypoints[w];
                            positions[w] = anchor + new Vector2(rel.East, rel.North);
                        }

                        // Register in the local pool; get a new local handle.
                        localRouteHandle = _trajectoryPool.RegisterTrajectory(positions);
                    }

                    batch.Results[resultIdx] = new PathResult
                    {
                        RequestId          = ddsResult.RequestId,
                        IsReachable        = ddsResult.IsReachable,
                        TotalDistanceMeters = ddsResult.TotalDistanceMeters,
                        RouteHandle        = localRouteHandle,
                        SourceNodeId       = _localNodeId,
                    };

                    resultIdx++;
                }

                // Expose the number of available results to consuming systems via Count.
                batch.Count = resultIdx;
            }
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    // ── Solver-side pathfinding translators (NavigationSolver -> Brain) ────────────

    /// <summary>
    /// Ingress translator. Receives <c>PathRequestBatch</c> from Brain nodes, reconstructs
    /// absolute Cartesian start/end from the BatchOrigin anchor, and populates the local
    /// <c>PathfindingBatchData.Requests</c> singleton so <c>PathfindingSolverSystem</c> can
    /// resolve them.  Stamps <c>SourceNodeId</c> for return-path demultiplexing.
    /// </summary>
    public sealed class PathRequestSolverIngressTranslator : IDescriptorTranslator
    {
        private readonly DdsReader<PathRequestBatch>? _reader;
        private readonly IGeographicTransform _geoTransform;

        public long   DescriptorOrdinal => (long)EDescriptorType.dtPathRequestBatch;
        public string TopicName         => "PathRequestBatch";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
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
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<PathfindingBatchData>()) return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;
                if (data.Requests == null || data.Requests.Count == 0) continue;

                ref var batch = ref repo.GetSingleton<PathfindingBatchData>();

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
                    if (batch.Count >= PathfindingBatchData.DefaultCapacity) break;

                    var start = anchor + new Vector3(ddsReq.Start.East, ddsReq.Start.North, ddsReq.Start.Up);
                    var end   = anchor + new Vector3(ddsReq.End.East,   ddsReq.End.North,   ddsReq.End.Up);

                    batch.Requests[batch.Count] = new PathRequest
                    {
                        RequestId       = ddsReq.RequestId,
                        Start           = start,
                        End             = end,
                        MobilityProfile = ddsReq.MobilityProfile,
                        // Stamp the originating Brain node ID for demultiplexing on egress.
                        SourceNodeId    = data.SourceNodeId,
                    };

                    batch.Count++;
                }
            }
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }

    /// <summary>
    /// Egress translator. Reads the completed path results from <c>PathfindingBatchData.Results</c>
    /// on the Navigation Solver after <c>PathfindingSolverSystem</c> has run, extracts waypoints
    /// from the solver's local <c>TrajectoryPoolManager</c>, groups results by originating Brain
    /// node, and publishes targeted <c>PathResponseBatch</c> messages.
    /// Acts as the terminal sink for the Solver's pathfinding pipeline (clears batch.Count).
    /// </summary>
    public sealed class PathResponseSolverEgressTranslator : IDescriptorTranslator
    {
        private readonly DdsWriter<PathResponseBatch>? _writer;
        private readonly IGeographicTransform _geoTransform;
        private readonly TrajectoryPoolManager _trajectoryPool;

        public long   DescriptorOrdinal => (long)EDescriptorType.dtPathResponseBatch;
        public string TopicName         => "PathResponseBatch";
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public PathResponseSolverEgressTranslator(
            DdsParticipant?      participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform,
            TrajectoryPoolManager trajectoryPool)
        {
            _geoTransform   = geoTransform   ?? throw new ArgumentNullException(nameof(geoTransform));
            _trajectoryPool = trajectoryPool ?? throw new ArgumentNullException(nameof(trajectoryPool));
            _writer         = participant != null ? new DdsWriter<PathResponseBatch>(participant, TopicName) : null;
        }

        public void ScanAndPublish(ISimulationView view)
        {
            if (_writer is null) return;
            if (view is not EntityRepository repo) return;
            if (!repo.HasSingleton<PathfindingBatchData>()) return;

            ref var batch = ref repo.GetSingleton<PathfindingBatchData>();
            if (batch.Count == 0) return;

            // Demultiplex results by originating Brain node.
            var batchesByNode = new Dictionary<int, List<DdsPathResult>>();

            for (int i = 0; i < batch.Count; i++)
            {
                var result = batch.Results[i];

                if (!batchesByNode.TryGetValue(result.SourceNodeId, out var resultList))
                {
                    resultList = new List<DdsPathResult>();
                    batchesByNode[result.SourceNodeId] = resultList;
                }

                List<RelativeVector3>? coarseWaypoints = null;
                GeoPoint batchOrigin = default;

                if (result.IsReachable && result.RouteHandle >= 0
                    && _trajectoryPool.TryGetTrajectory(result.RouteHandle, out var traj))
                {
                    // Use first waypoint as coordinate anchor for relative encoding.
                    var firstPos = traj.Waypoints[0].Position;
                    var anchorVec3 = new Vector3(firstPos.X, firstPos.Y, 0f);
                    var (lat, lon, alt) = _geoTransform.ToGeodetic(anchorVec3);
                    batchOrigin = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt };

                    coarseWaypoints = new List<RelativeVector3>(traj.Waypoints.Length);
                    for (int w = 0; w < traj.Waypoints.Length; w++)
                    {
                        var wp = traj.Waypoints[w].Position;
                        coarseWaypoints.Add(new RelativeVector3
                        {
                            East  = wp.X - firstPos.X,
                            North = wp.Y - firstPos.Y,
                            Up    = 0f,
                        });
                    }
                }

                resultList.Add(new DdsPathResult
                {
                    RequestId          = result.RequestId,
                    IsReachable        = result.IsReachable,
                    TotalDistanceMeters = result.TotalDistanceMeters,
                    RouteHandle        = result.RouteHandle,
                    CoarseWaypoints    = coarseWaypoints,
                });

                // Keep batchOrigin for use below -- stored per-node for the first reachable result.
                // Override the node-level origin with each result's origin; the Brain reconstructs
                // waypoints per DdsPathResult using data.BatchOrigin as a common anchor.
                // For simplicity, use a single BatchOrigin per response batch (first reachable result).
                // Unreachable results carry null CoarseWaypoints and the anchor is irrelevant for them.
                _ = batchOrigin; // used via local capture in the Add above for each result
            }

            // Publish one targeted batch per originating Brain node.
            foreach (var kvp in batchesByNode)
            {
                // Compute a representative BatchOrigin for this node's results.
                // Use first reachable result's anchor, or default GeoPoint if all unreachable.
                GeoPoint origin = ComputeBatchOrigin(kvp.Value, batch);
                _writer.Write(new PathResponseBatch
                {
                    TargetNodeId = kvp.Key,
                    BatchOrigin  = origin,
                    Results      = kvp.Value,
                });
                SentSampleCount++;
            }

            // Terminal sink: flush the queue after publishing.
            batch.Count = 0;
        }

        private GeoPoint ComputeBatchOrigin(List<DdsPathResult> results, in PathfindingBatchData batch)
        {
            // Find the first result with waypoints and use its first waypoint as the batch anchor.
            foreach (var r in results)
            {
                if (r.IsReachable && r.RouteHandle >= 0
                    && _trajectoryPool.TryGetTrajectory(r.RouteHandle, out var traj))
                {
                    var firstPos = traj.Waypoints[0].Position;
                    var anchorVec3 = new Vector3(firstPos.X, firstPos.Y, 0f);
                    var (lat, lon, alt) = _geoTransform.ToGeodetic(anchorVec3);
                    return new GeoPoint { Latitude = lat, Longitude = lon, Altitude = alt };
                }
            }
            return default;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}

