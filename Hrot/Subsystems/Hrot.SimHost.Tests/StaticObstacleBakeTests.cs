using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3136</c> P-7a (O2, R-243, R-244) — <see cref="StaticObstacleBakeSystem"/>, the terrain lifecycle participant: a static
    /// obstacle is baked into the node's terrain in the BACKGROUND, after a WALL-CLOCK quiet period, in ONE bake for everything that
    /// arrived together — and its construction is acked only after that bake has COMMITTED. 📄 docs/DESIGN_Peek_And_Fire.md §9.2 O2, §9.4.
    /// </summary>
    public sealed class StaticObstacleBakeTests : IDisposable
    {
        private readonly EntityRepository _world = new();
        private readonly string _staging = Path.Combine(Path.GetTempPath(), "p7a-" + Guid.NewGuid().ToString("N"));
        private double _wall;

        public StaticObstacleBakeTests()
        {
            SimHostComponentRegistry.RegisterAll(_world);
            Directory.CreateDirectory(_staging);
        }

        public void Dispose()
        {
            _world.Dispose();
            try { Directory.Delete(_staging, recursive: true); } catch { }
        }

        private StaticObstacleBakeSystem System() => new(() => _wall);

        /// <summary>A node holding a terrain (flat, empty): the residency publishes its bakery.</summary>
        private TerrainResidency Terrain()
        {
            var residency = new TerrainResidency(_staging, new RoadNetworkHolder());
            residency.Unload(_world);   // ⭐ publishes the bakery over an empty base world
            Assert.True(_world.HasSingletonManaged<StaticObstacleBakery>());
            return residency;
        }

        private Entity Obstacle(float x, string material = "car-body")
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = new Vector3(x, 0, 0), Rotation = Quaternion.Identity });
            _world.AddComponent(e, new StaticObstacle { Material = new FixedString32(material) });
            _world.AddComponent(e, new ObstacleShape { Length = 4.5f, Width = 1.8f, Height = 1.5f });
            _world.SetLifecycleState(e, EntityLifecycle.Constructing);
            return e;
        }

        private int[] Acked()
        {
            _world.Bus.SwapBuffers();
            return _world.Bus.Read<ConstructionAck>().ToArray()
                .Where(a => a.ModuleId == TerrainObstacles.LifecycleModuleId).Select(a => a.Entity.Index).ToArray();
        }

        private void RunUntilBaked(StaticObstacleBakeSystem sys, int bakes)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (sys.Bakes < bakes && DateTime.UtcNow < deadline)
            {
                sys.Execute(_world, 0.016f);
                if (sys.Bakes < bakes) Thread.Sleep(5);
            }
            Assert.Equal(bakes, sys.Bakes);
        }

        /// <summary>
        /// ⭐⭐⭐ <c>P7a_B1</c> — THE GATE: nothing is acked before the quiet period, nor while the bake runs; the ack comes only after the
        /// bake has committed — the node's world then holds the obstacle as a prism of its material, and the cover database has points
        /// round it. 🔴 Red-proof: ack the batch at bake START (move the Ack loop above <c>CommitObstacles</c>) and the "nothing acked
        /// while baking" assertion fails.
        /// </summary>
        [Fact]
        public void P7a_B1_AnObstacleIsAckedOnlyAfterItsBakeCommitted_AfterTheWallClockQuietPeriod()
        {
            Terrain();
            var sys = System();
            var car = Obstacle(10f);

            sys.Execute(_world, 0.016f);
            _wall = 0.4; sys.Execute(_world, 0.016f);
            Assert.False(sys.Baking, "inside the quiet period nothing is baked");
            Assert.Empty(Acked());

            _wall = 0.6; sys.Execute(_world, 0.016f);
            Assert.True(sys.Baking || sys.Bakes == 0, "the bake starts after the quiet period");
            Assert.Empty(Acked());

            RunUntilBaked(sys, 1);
            Assert.Contains(car.Index, Acked());

            var world = _world.GetSingletonManaged<TerrainWorld>()!;
            var prism = Assert.Single(world.Prisms);
            Assert.Equal("car-body", prism.Material!.Name);
            Assert.StartsWith(TerrainObstacles.LabelPrefix, prism.Label);
            Span<CoverPoint> pts = stackalloc CoverPoint[32];
            Assert.True(_world.GetSingletonManaged<ICoverProvider>()!.GetCoverPointsInRadius(new Vector2(10, 0), 5f, pts) > 0);
        }

        /// <summary>⭐⭐ <c>P7a_B2</c> — obstacles arriving in quick succession (a scenario's) are baked in ONE bake, all acked together.</summary>
        [Fact]
        public void P7a_B2_ObstaclesArrivingTogether_AreBakedOnce()
        {
            Terrain();
            var sys = System();
            var a = Obstacle(10f);
            sys.Execute(_world, 0.016f);
            _wall = 0.2; var b = Obstacle(20f, "sandbags"); sys.Execute(_world, 0.016f);
            _wall = 0.4; var c = Obstacle(30f, "concrete"); sys.Execute(_world, 0.016f);
            Assert.Equal(0, sys.Bakes);
            _wall = 1.0;
            RunUntilBaked(sys, 1);
            var acked = Acked();
            Assert.Contains(a.Index, acked); Assert.Contains(b.Index, acked); Assert.Contains(c.Index, acked);
            Assert.Equal(3, _world.GetSingletonManaged<TerrainWorld>()!.Prisms.Count);
        }

        /// <summary>⭐ <c>P7a_B3</c> — a node holding no terrain (no bakery) has nothing to bake: the obstacle is acked at once.</summary>
        [Fact]
        public void P7a_B3_ANodeWithNoTerrain_AcksAtOnce()
        {
            var sys = System();
            var car = Obstacle(10f);
            sys.Execute(_world, 0.016f);
            Assert.Contains(car.Index, Acked());
            Assert.Equal(0, sys.Bakes);
        }

        /// <summary>⭐ <c>P7a_B4</c> — removing the obstacle re-bakes the world without it (a runtime change, debounced the same way).</summary>
        [Fact]
        public void P7a_B4_RemovingAnObstacle_RebakesWithoutIt()
        {
            Terrain();
            var sys = System();
            var car = Obstacle(10f);
            sys.Execute(_world, 0.016f);
            _wall = 1.0;
            RunUntilBaked(sys, 1);
            _world.SetLifecycleState(car, EntityLifecycle.Active);

            _world.DestroyEntity(car);
            _wall = 1.1; sys.Execute(_world, 0.016f);
            _wall = 2.0;
            RunUntilBaked(sys, 2);
            Assert.Empty(_world.GetSingletonManaged<TerrainWorld>()!.Prisms);
        }
    }
}
