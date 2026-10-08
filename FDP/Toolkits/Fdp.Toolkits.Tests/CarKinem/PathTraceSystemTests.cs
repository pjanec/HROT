using System;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkit.CarKinem.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3117</c> (R-226) — the planned path the map draws is RECORDED state: <see cref="PathTraceSystem"/> copies the
    /// trajectory a mover follows into its <see cref="PathTrace"/>, only when it changed, thinning a long one but never dropping a door
    /// step or the end. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a.
    /// </summary>
    public sealed class PathTraceSystemTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        private readonly TrajectoryPoolManager _pool = new();
        private readonly PathTraceSystem _sys;

        public PathTraceSystemTests()
        {
            _w.RegisterComponent<NavState>();
            _w.RegisterComponent<PathTrace>();
            _sys = new PathTraceSystem(_pool);
        }

        public void Dispose() { _pool.Dispose(); _w.Dispose(); }

        private Entity Mover(int trajectoryId, float progress = 0f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new NavState { Mode = KinematicsMode.CustomTrajectory, TrajectoryId = trajectoryId, ProgressS = progress });
            return e;
        }

        [Fact]
        public void CE3117_ALongPath_IsThinned_KeepingTheEndsAndEveryDoorStep()
        {
            var pts = Enumerable.Range(0, 100).Select(i => new Vector3(i, 0, 0)).ToArray();
            var traversals = new byte[100];
            traversals[37] = traversals[61] = 3;   // TraversalKind.Door
            _pool.RegisterTrajectoryWithKey(pts, 5, traversals);
            var mover = Mover(5);

            _sys.Execute(_w, 0.1f);

            var t = _w.GetComponentRO<PathTrace>(mover);
            var kept = t.PointsRO().ToArray();
            Assert.InRange(kept.Length, 20, PathTrace.Capacity);                 // thinned to fit
            Assert.Equal(0f, kept[0].Position.X);
            Assert.Equal(99f, kept[^1].Position.X);                          // the end of the path is never thinned away
            Assert.Contains(kept, p => p.Position.X == 37f && p.Traversal == 3);
            Assert.Contains(kept, p => p.Position.X == 61f && p.Traversal == 3);
            Assert.True(kept.Zip(kept.Skip(1)).All(p => p.Second.S > p.First.S), "distances ascend");
            Assert.Equal(new Vector3(42.5f, 0, 0), t.Sample(42.5f));         // sampled by distance along the path
        }

        [Fact]
        public void CE3117_TheTrace_IsRewrittenOnlyWhenThePathChanges_AndClearedWhenTheMoverStopsFollowing()
        {
            _pool.RegisterTrajectoryWithKey(new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 0) }, 7);
            var mover = Mover(7);
            _sys.Execute(_w, 0.1f);
            Assert.Equal(2, _w.GetComponentRO<PathTrace>(mover).Count);

            _w.Tick();
            uint stamped = _w.GetComponentTable<PathTrace>().GetVersionForEntity(mover.Index);
            _sys.Execute(_w, 0.1f);                                                // same path: no write, so not re-recorded
            Assert.Equal(stamped, _w.GetComponentTable<PathTrace>().GetVersionForEntity(mover.Index));

            _pool.RegisterTrajectoryWithKey(new[] { new Vector3(0, 0, 0), new Vector3(5, 5, 0), new Vector3(10, 0, 0) }, 7);   // a replan in place
            _sys.Execute(_w, 0.1f);
            Assert.Equal(3, _w.GetComponentRO<PathTrace>(mover).Count);

            ref var nav = ref _w.GetComponentRW<NavState>(mover);
            nav.Mode = KinematicsMode.None;
            _sys.Execute(_w, 0.1f);
            Assert.Equal(0, _w.GetComponentRO<PathTrace>(mover).Count);
            Assert.Equal(-1, _w.GetComponentRO<PathTrace>(mover).TrajectoryId);
        }
    }
}
