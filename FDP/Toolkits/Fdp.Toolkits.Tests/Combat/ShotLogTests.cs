using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐ Tuning T-4 — the shot log (📄 docs/DESIGN_Terrain_Combat_Tuning.md §4): the systems that decide a round write its record,
    /// and a round that hits a unit in FRONT of a wall never lists the wall (its segment's crossings are held until the raycast
    /// resolves).
    /// </summary>
    public sealed class ShotLogTests : System.IDisposable
    {
        private readonly EntityRepository _world = new();

        public ShotLogTests()
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<SimVelocity>();
            _world.RegisterComponent<BallisticProjectile>();
            _world.RegisterComponent<PhysicsCollider>();
            _world.RegisterEvent<RaycastRequestEvent>();
            _world.RegisterEvent<RaycastResultEvent>();
            _world.RegisterEvent<HitEvent>();
            _world.RegisterEvent<DetonationNotification>();
            _world.SetSingleton(new GlobalTime { FrameNumber = 0, TimeScale = 1f });
            _world.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "wall", "height": 2, "thickness": 0.2, "material": "concrete", "label": "W" }, "geometry": { "type": "LineString", "coordinates": [[0,10],[10,10]] } } ] }
            """));
        }

        public void Dispose() => _world.Dispose();

        private (Entity Bullet, ShotRecord Shot) Fire(Vector3 from, Vector3 to)
        {
            var b = _world.CreateEntity();
            _world.AddComponent(b, new SimTransform { Position = to, Rotation = Quaternion.Identity });
            _world.AddComponent(b, new BallisticProjectile { PreviousPosition = from, Damage = 25f, Penetration = 5f });
            return (b, ShotLog.For(_world).Add(new ShotRecord { Bullet = b, Muzzle = from, Damage = 25f, Penetration = 5f }));
        }

        private void Ballistics()
        {
            new BallisticsSystem().Execute(_world, 0.016f);
            ((EntityCommandBuffer)((ISimulationView)_world).GetCommandBuffer()).Playback(_world);
            _world.Bus.SwapBuffers();
        }

        [Fact]
        public void TheRing_KeepsTheNewest256_NewestFirst_AndForgetsTheEvicted()
        {
            var log = ShotLog.For(_world);
            var first = log.Add(new ShotRecord { Bullet = new Entity(1, 1) });
            for (int i = 2; i <= 300; i++) log.Add(new ShotRecord { Bullet = new Entity(i, 1) });
            Assert.Equal(300, log.Count);
            var recent = log.Recent(1000);
            Assert.Equal(ShotLog.Capacity, recent.Count);
            Assert.Equal(299, recent[0].Seq);
            Assert.Null(log.Of(first.Bullet));
            Assert.Same(ShotLog.For(_world), log);
            Assert.Null(ShotLog.Peek(new EntityRepository()));   // readers never create one
        }

        [Fact]
        public void AStoppedRound_IsRecordedAsStoppedByTerrain_AtTheWall_WithTheCrossingThatStoppedIt_AfterTheGrace()
        {
            var (_, shot) = Fire(new Vector3(5, 0, 1.6f), new Vector3(5, 15, 1.6f));
            Ballistics();
            Assert.Equal(ShotOutcome.InFlight, shot.Outcome);     // ⚠ kept for the grace: a unit in front of the wall may still be struck
            _world.SetSingleton(new GlobalTime { FrameNumber = CombatConstants.StoppedRoundGraceTicks + 1, TimeScale = 1f });
            Ballistics();
            Assert.Equal(ShotOutcome.StoppedByTerrain, shot.Outcome);
            var c = Assert.Single(shot.Crossings);                 // carried from the muzzle to the wall
            Assert.Equal("concrete", c.Material);
            Assert.False(c.Passed);
            Assert.Equal(9.9f, shot.EndPoint!.Value.Y, 3);
        }

        /// <summary>⭐ The latency case: the round has ALREADY been stopped at the wall (and frozen) when the raycast of its last segment
        /// resolves with a unit in front of the wall — the unit is still struck, with the full round, and the wall is not listed.</summary>
        [Fact]
        public void ARoundThatHitsAUnitInFrontOfTheWall_IsAHit_WithTheFullRound_AndNeverListsTheWall()
        {
            var (bullet, shot) = Fire(new Vector3(5, 0, 1.6f), new Vector3(5, 15, 1.6f));
            Ballistics();
            Assert.NotEqual(0u, _world.GetComponent<BallisticProjectile>(bullet).StoppedTick);
            var unit = _world.CreateEntity();
            _world.Bus.Publish(new RaycastResultEvent { Hit = new RaycastHit
            {
                HasHit = 1, RayId = PhysicsConstants.PackBulletRayId(bullet.Index), HitEntity = unit,
                Start = new Vector3(5, 0, 1.6f), End = new Vector3(5, 9.9f, 1.6f), T = 0.5f,
            } });
            _world.Bus.SwapBuffers();
            new HitResolutionSystem().Execute(_world, 0.016f);
            Assert.Equal(ShotOutcome.Hit, shot.Outcome);
            Assert.Equal(unit, shot.HitEntity);
            Assert.Equal(25f, shot.ArrivingDamage);
            Assert.Equal(5f, shot.ArrivingPenetration);
            Assert.Empty(shot.Crossings);                         // the wall is beyond the unit
            _world.Bus.SwapBuffers();
            var det = Assert.Single(_world.Bus.Read<DetonationNotification>().ToArray());
            Assert.Equal(25f, det.Damage);
        }

        [Fact]
        public void ARoundThatOutlivesItsLifetime_IsExpired()
        {
            var (_, shot) = Fire(new Vector3(50, 0, 1.6f), new Vector3(50, 15, 1.6f));
            _world.SetSingleton(new GlobalTime { FrameNumber = CombatConstants.BulletLifetimeTicks + 1, TimeScale = 1f });
            Ballistics();
            Assert.Equal(ShotOutcome.Expired, shot.Outcome);
        }
    }
}
