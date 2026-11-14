using System;
using System.IO;
using System.Numerics;
using System.Threading;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4 part 2; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md D-1, D-2, V-4, V-6) — the bake CACHES the craters: once a
    /// <see cref="GroundDamage"/> record is baked, the resident world's height grid holds the crater (the navmesh and the 3-D mesh follow) while the simulation's
    /// answer stays the bare ground + the view's craters (never counted twice); every vehicle routes round it and infantry crosses (D-2); the dirty rectangle is
    /// the crater's disc (V-4); a display node bakes the ground too (V-6); the record gone (a seek back) ⇒ the ground comes back.
    /// </summary>
    public sealed class CraterBakeTests : IDisposable
    {
        private readonly EntityRepository _world = new();
        private readonly string _staging = Path.Combine(Path.GetTempPath(), "p4-" + Guid.NewGuid().ToString("N"));
        private readonly SwitchableNavmeshProvider _node = new();
        private readonly TerrainWorld _flat;
        private double _wall;
        private static readonly Crater Hole = new(new Vector2(30f, 30f), 4f, 1.8f, 0.3f, (SimInstant)1.0, CraterKind.Large);
        /// <summary>A WIDE, SHALLOW crater: its walls (≈ 8°) are gentle enough for the vehicle layer's 20° slope limit, so only the crater AREA keeps a vehicle out.</summary>
        private static readonly Crater Dish = new(new Vector2(30f, 30f), 6f, 0.4f, 0.05f, (SimInstant)1.0, CraterKind.Large);

        public CraterBakeTests()
        {
            SimHostComponentRegistry.RegisterAll(_world);
            Directory.CreateDirectory(_staging);
            int n = 61;   // a flat 60 × 60 m field, 1 m posts
            _flat = new TerrainWorld
            {
                Name = "range", BoundsMin = Vector2.Zero, BoundsMax = new Vector2(60f, 60f),
                Height = new TerrainHeightGrid(Vector2.Zero, 1f, n, n, new float[n * n]),
            };
        }

        public void Dispose()
        {
            _world.Dispose();
            try { Directory.Delete(_staging, recursive: true); } catch { }
        }

        private StaticObstacleBakeSystem Sys() => new(() => _wall);

        private TerrainResidency Node(bool muscle)
        {
            var residency = new TerrainResidency(_staging, new RoadNetworkHolder());
            INavmeshProvider? baseline = null;
            if (muscle)
            {
                var factory = new RecastNavmeshFactory { Layers = NavLayerMask.Infantry | NavLayerMask.Vehicle, TileCache = new NavTileCache() };
                baseline = factory.Build(_flat);
                residency.AttachNavmesh(factory, _node);
            }
            residency.Commit(_world, new TerrainResidency.Staged { Definition = new TerrainDefinition { Name = "range" }, World = _flat, TerrainName = "range", Navmesh = baseline });
            return residency;
        }

        private Entity Record(in Crater c)
        {
            var d = new GroundDamage();
            (d.CellX, d.CellY) = GroundDamages.CellOf(c.Centre);
            GroundDamages.Add(ref d, in c, out _);
            var e = _world.CreateEntity();
            _world.AddComponent(e, d);
            return e;
        }

        private void Bake(StaticObstacleBakeSystem sys, int bakes)
        {
            sys.Execute(_world, 0.016f);
            _wall += 0.6;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (sys.Bakes < bakes && DateTime.UtcNow < deadline)
            {
                sys.Execute(_world, 0.016f);
                if (sys.Bakes < bakes) Thread.Sleep(5);
            }
            Assert.Equal(bakes, sys.Bakes);
        }

        private TerrainWorld Resident => _world.GetSingletonManaged<TerrainWorld>()!;

        private static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.LengthSquared();
            float t = l2 > 1e-9f ? Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>
        /// ⭐⭐ D-1 — a crater on a muscle: the bake lays it into the resident grid (the cache) and the simulation's ground is read ONCE (bare + the view's craters).
        /// 🔴 Red-proof: read <c>World.GroundHeightAt</c> (the cache) in <c>TerrainWorldQuery.GroundHeightAt</c> instead of the view's answer ⇒ the CraterTests
        /// same-tick rail goes red; here, adding the offset to the cache ⇒ the centre reads twice the depth.
        /// </summary>
        [Fact]
        public void ACrater_IsBakedIntoTheGround_AndTheGroundIsCountedOnce()
        {
            Node(muscle: true);
            var sys = Sys();
            Record(Hole);
            Bake(sys, 1);

            var w = Resident;
            Assert.Same(DoorStates.Of(_world, w).Craters, w.HeightCraters);
            Assert.Equal(-1.8f, w.GroundHeightAt(30f, 30f), 3);                              // the cache: the grid holds the bowl
            Assert.Equal(0f, w.BareGroundHeightAt(30f, 30f), 3);
            var q = Fdp.Toolkit.Terrain.TerrainWorldQuery.ForView(_world, w);
            Assert.Equal(-1.8f, q.GroundHeightAt(30f, 30f), 3);                              // the truth: once, not twice
            Assert.Equal(-1.8f, q.SurfaceZ(30f, 30f, 0f), 3);
        }

        /// <summary>
        /// ⭐⭐ D-2 (R-4233) — a WIDE, SHALLOW crater (walls ≈ 8°, under the vehicle layer's 20° limit, so the slope alone would let a vehicle through): a vehicle's
        /// path goes ROUND its bowl and an infantry path goes straight across. 🔴 Red-proof: drop <c>NavDoorways.Craters</c> from the factory's volumes ⇒ the vehicle
        /// path cuts across the bowl.
        /// </summary>
        [Fact]
        public void EveryVehicleRoutesRoundACrater_EvenAShallowOne_AndInfantryCrosses()
        {
            Node(muscle: true);
            var sys = Sys();
            Record(Dish);
            Bake(sys, 1);

            Vector3 south = new(30f, 5f, 0f), north = new(30f, 55f, 0f);
            Span<NavWaypoint> wps = stackalloc NavWaypoint[128];
            int nv = _node.PlanPath(south, north, wps, (uint)NavLayerMask.Vehicle);
            Assert.True(nv >= 2, "a vehicle still gets across the field");
            float closest = float.MaxValue;
            for (int i = 0; i + 1 < nv; i++)
                closest = MathF.Min(closest, Distance(Dish.Centre, new Vector2(wps[i].Position.X, wps[i].Position.Y), new Vector2(wps[i + 1].Position.X, wps[i + 1].Position.Y)));
            Assert.True(closest >= Dish.Radius, $"the vehicle path passes {closest:0.00} m from the crater's centre (bowl radius {Dish.Radius})");

            int ni = _node.PlanPath(south, north, wps, (uint)NavLayerMask.Infantry);
            Assert.True(ni >= 2);
            float length = 0f;
            for (int i = 0; i + 1 < ni; i++) length += Vector3.Distance(wps[i].Position, wps[i + 1].Position);
            Assert.True(length < 52f, $"infantry crosses the crater: {length:0.0} m");
        }

        /// <summary>
        /// ⭐ V-4 / V-6 / G-8 — a display node (no navmesh) bakes the crater into its ground too; the dirty rectangle of the first diffed bake is the crater's disc; the record
        /// gone (a seek back) ⇒ the next bake restores the bare ground and dirties the same disc. 🔴 Red-proof: skip <c>CraterDirt</c> ⇒ no rectangle.
        /// </summary>
        [Fact]
        public void ADisplayNode_BakesTheCrater_ItsDiscIsTheDirtyRectangle_AndASeekBackRestoresTheGround()
        {
            var residency = Node(muscle: false);
            var sys = Sys();
            Assert.False(residency.BakesDamage);
            var car = Record(new Crater(new Vector2(5f, 5f), 1f, 0.4f, 0.1f, (SimInstant)0.5, CraterKind.Small));   // a first, small crater: the baseline bake
            Bake(sys, 1);
            Assert.Equal(-0.4f, Resident.GroundHeightAt(5f, 5f), 3);

            var e = Record(Hole);
            Bake(sys, 2);
            Assert.Equal(-1.8f, Resident.GroundHeightAt(30f, 30f), 3);
            var dirty = Assert.Single(sys.LastDirty!);
            float o = Hole.OuterRadius;
            Assert.Equal(Hole.Centre - new Vector2(o), dirty.Min);
            Assert.Equal(Hole.Centre + new Vector2(o), dirty.Max);

            _world.DestroyEntity(e);   // the keyframe before the blast
            Bake(sys, 3);
            Assert.Equal(0f, Resident.GroundHeightAt(30f, 30f), 3);
            Assert.Equal(-0.4f, Resident.GroundHeightAt(5f, 5f), 3);
            Assert.Single(sys.LastDirty!);
        }
    }
}
