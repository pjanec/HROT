using System;
using System.Numerics;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.ModuleHost.Abstractions;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Services;
using Hrot.Network.NED.SimHost;
using Xunit;

namespace Hrot.Network.NED.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3129</c> — the scale-out path request/response wire (a NavigationSolver on another node), without DDS: the four
    /// translators' encode/decode halves. 📄 docs/designs/navig-2/Navigation_Design_v2_0.md §5.2a.
    /// </summary>
    public sealed class PathfindingTranslatorsTests
    {
        private const int BrainNode = 7;

        private static WGS84Transform Geo() => new(52.52, 13.405, 0.0);

        /// <summary>
        /// The actor's order — forced backend, navmesh layer and road use — crosses the wire. 🔴 Before: the request carried
        /// <c>MobilityProfile</c> only, so a solver on another node planned every request as Auto / all layers / Unspecified.
        /// </summary>
        [Fact]
        public void CE3129_TheOrder_BackendLayerAndRoadUse_CrossesTheWire()
        {
            var geo = Geo();
            var egress = new PathRequestBrainEgressTranslator(participant: null, new NetworkEntityMap(), geo, BrainNode);
            var ingress = new PathRequestSolverIngressTranslator(participant: null, new NetworkEntityMap(), geo);

            var sent = new PathfindingRequestEvent
            {
                RequestId = 42, SourceNodeId = BrainNode, MobilityProfile = 2,
                Start = new Vector3(10f, 20f, 0f), End = new Vector3(110f, 60f, 0f),
                BackendForce = NavigationBackend.NavRoadGraph, NavLayerMask = (int)NavLayerMask.Vehicle, RoadUse = RoadUse.Never,
            };
            var batch = egress.BuildBatch(new[] { sent });
            Assert.NotNull(batch);

            using var repo = new EntityRepository();
            repo.RegisterEvent<PathfindingRequestEvent>();
            var view = (ISimulationView)repo;
            var cmd = view.GetCommandBuffer();
            ingress.ProcessBatch(batch!.Value, cmd, view);
            ((EntityCommandBuffer)cmd).Playback(repo);
            repo.Bus.SwapBuffers();

            var got = Assert.Single(view.ReadEvents<PathfindingRequestEvent>().ToArray());
            Assert.Equal(42, got.RequestId);
            Assert.Equal(BrainNode, got.SourceNodeId);
            Assert.Equal(NavigationBackend.NavRoadGraph, got.BackendForce);
            Assert.Equal((int)NavLayerMask.Vehicle, got.NavLayerMask);
            Assert.Equal(RoadUse.Never, got.RoadUse);
            Assert.InRange(Vector3.Distance(sent.End, got.End), 0f, 0.05f);
        }

        /// <summary>
        /// Two routes answered to one Brain in one frame both arrive where the solver planned them. 🔴 Before: each route was
        /// encoded relative to its OWN first waypoint while the batch carried the LAST route's, so the first route landed
        /// displaced by the distance between the two starts (here 300 m).
        /// </summary>
        [Fact]
        public void CE3129_TwoRoutesInOneBatch_BothArriveWhereTheSolverPlannedThem()
        {
            var geo = Geo();
            using var solverPool = new TrajectoryPoolManager();
            var routeA = new[] { new Vector3(0f, 0f, 0f), new Vector3(50f, 0f, 0f) };
            var routeB = new[] { new Vector3(300f, 0f, 0f), new Vector3(300f, 80f, 0f), new Vector3(340f, 80f, 0f) };
            solverPool.RegisterTrajectoryWithKey(routeA, 1);
            solverPool.RegisterTrajectoryWithKey(routeB, 2);

            var egress = new PathResponseSolverEgressTranslator(participant: null, new NetworkEntityMap(), geo, solverPool);
            var batches = egress.BuildBatches(new[]
            {
                new PathfindingResultEvent { RequestId = 11, IsReachable = true, RouteHandle = 1, SourceNodeId = BrainNode },
                new PathfindingResultEvent { RequestId = 12, IsReachable = true, RouteHandle = 2, SourceNodeId = BrainNode },
            });
            var batch = Assert.Single(batches);

            using var repo = new EntityRepository();
            repo.SetSingleton(new PathfindingBatchData
            {
                Results = new NativeArray<PathResult>(PathfindingBatchData.DefaultCapacity, Allocator.Persistent),
            });
            using var brainPool = new TrajectoryPoolManager();
            var ingress = new PathResponseBrainIngressTranslator(participant: null, new NetworkEntityMap(), geo, brainPool, BrainNode);
            ingress.ProcessBatch(batch, repo);

            ref var results = ref repo.GetSingleton<PathfindingBatchData>();
            AssertRoute(brainPool, results.Results[11], routeA);
            AssertRoute(brainPool, results.Results[12], routeB);
            results.Results.Dispose();
        }

        private static void AssertRoute(TrajectoryPoolManager pool, PathResult result, Vector3[] planned)
        {
            Assert.True(result.IsReachable);
            Assert.True(pool.TryGetTrajectory(result.RouteHandle, out var traj));
            Assert.Equal(planned.Length, traj.Waypoints.Length);
            for (int i = 0; i < planned.Length; i++)
                Assert.True(Vector3.Distance(planned[i], traj.Waypoints[i].Position) < 0.05f,
                    $"waypoint {i}: planned {planned[i]}, arrived at {traj.Waypoints[i].Position}");
        }
    }
}
