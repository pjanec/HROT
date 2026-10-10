using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐ Buildings programme Stage 6 (<c>CE-1032</c>, R-225 W-3/W-4/W-8) — a warhead round's flight: the arc launch reaches its point
    /// (high for a launcher, low for a throw), an impact-fuzed bomb bursts ON the roof it falls on (just above it, Target none, the
    /// munition carried), a time-fuzed grenade lands and waits for its fuze, and a burst round never bursts twice.
    /// 📄 docs/DESIGN_Building_Interiors.md §3k.
    /// </summary>
    public sealed class WarheadFlightTests : IDisposable
    {
        private const float Dt = 1f / 60f;
        private readonly EntityRepository _w;
        private readonly BallisticsSystem _sys = new();

        public WarheadFlightTests()
        {
            _w = new EntityRepository();
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<BallisticProjectile>();
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterEvent<RaycastRequestEvent>();
            _w.RegisterEvent<DetonationNotification>();
            _w.SetSingleton(new GlobalTime { FrameNumber = 1, TimeScale = 1f });
        }

        public void Dispose() => _w.Dispose();

        private Entity Launch(Vector3 from, Vector3 to, float speed, bool high, byte warhead, float fuze = 0f)
        {
            var v = FireProcessingSystem.ArcLaunch(from, to, speed, high);
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = from, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity { Linear = v });
            _w.AddComponent(e, new BallisticProjectile
            {
                PreviousPosition = from, Muzzle = from, SpawnTick = 1, TerrainFlags = 1, Ammo = 6002,
                Warhead = warhead, FuzeRemaining = fuze,
            });
            return e;
        }

        /// <summary>Runs the flight: integrate (LinearKinematicsSystem's pos += v·dt), then the ballistics step; returns every burst and
        /// the tick it happened at.</summary>
        private List<(uint Tick, DetonationNotification D)> Fly(int maxTicks)
        {
            var bursts = new List<(uint, DetonationNotification)>();
            for (uint tick = 2; tick < maxTicks; tick++)
            {
                _w.SetSingleton(new GlobalTime { FrameNumber = tick, TimeScale = 1f });
                foreach (var e in _w.Query().With<SimTransform>().With<SimVelocity>().Build())
                {
                    ref var tf = ref _w.GetComponentRW<SimTransform>(e);
                    tf.Position += _w.GetComponentRO<SimVelocity>(e).Linear * Dt;
                }
                _sys.Execute(_w, Dt);
                ((EntityCommandBuffer)((ISimulationView)_w).GetCommandBuffer()).Playback(_w);
                _w.Bus.SwapBuffers();
                foreach (var d in _w.Bus.Read<DetonationNotification>()) bursts.Add((tick, d));
            }
            return bursts;
        }

        [Theory]
        [InlineData(15f, 20f, false)]    // a throw
        [InlineData(60f, 100f, true)]    // a mortar on a reduced charge
        public void W8_TheArcLaunch_ReachesItsPoint_HighForALauncher_LowForAThrow(float speed, float range, bool high)
        {
            var from = new Vector3(0, 0, 1.5f);
            var v = FireProcessingSystem.ArcLaunch(from, new Vector3(range, 0, 0), speed, high);
            Assert.Equal(speed, v.Length(), 2);
            float elevation = MathF.Asin(v.Z / speed) * 180f / MathF.PI;
            Assert.True(high ? elevation > 60f : elevation < 45f, $"elevation {elevation}°");

            Launch(from, new Vector3(range, 0, 0), speed, high, WarheadRound.Area | WarheadRound.Arc);
            var bursts = Fly(60 * 30);
            var b = Assert.Single(bursts).D;
            Assert.InRange(b.HitX, range - 1.5f, range + 1.5f);   // Euler at 60 Hz: within a metre or so
            Assert.InRange(b.HitZ, 0f, 0.2f);                     // on the ground (the terrain query does not count it)
            Assert.Equal(Entity.Null, b.Target);
            Assert.Equal(6002, b.Ammo);
        }

        [Fact]
        public void W3_AnImpactFuzedBomb_BurstsOnTheRoof_JustAboveIt_AndNeverTwice()
        {
            _w.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse("""
                {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,200,200],"groundZ":0},"features":[
                  {"type":"Feature","properties":{"kind":"building","label":"R","building":{
                     "footprint":[[0,0],[10,0],[10,8],[0,8]],
                     "storeys":[{"height":3,"walls":[
                          {"from":[0,0],"to":[10,0]},{"from":[10,0],"to":[10,8]},{"from":[10,8],"to":[0,8]},{"from":[0,8],"to":[0,0]}]}]}},
                   "geometry":{"type":"Point","coordinates":[100,20]}}]}
                """));
            var bomb = Launch(new Vector3(0, 24, 0.5f), new Vector3(105, 24, 3f), 60f, high: true, WarheadRound.Area | WarheadRound.Arc);
            var bursts = Fly(60 * 30);
            var b = Assert.Single(bursts).D;
            Assert.InRange(b.HitX, 100.5f, 109.5f);                // on the roof, not beside the house
            Assert.InRange(b.HitZ, 3.0f, 3.0f + AreaEffect.BurstStandOffMetres + 0.05f);   // just above the slab
            Assert.Equal(Entity.Null, b.Target);
        }

        [Fact]
        public void W4_ATimeFuzedGrenade_LandsAndWaits_ThenBurstsWhenItsFuzeRunsOut()
        {
            Launch(new Vector3(0, 0, 1.5f), new Vector3(15, 0, 0), 15f, high: false,
                WarheadRound.Area | WarheadRound.Arc | WarheadRound.TimeFuze, fuze: 4.5f);
            var bursts = Fly(60 * 10);
            var (tick, b) = Assert.Single(bursts);
            Assert.InRange(tick * Dt, 4.4f, 4.7f);                // the fuze, not the landing (~1 s)
            Assert.InRange(b.HitX, 13.5f, 16.5f);
            Assert.InRange(b.HitZ, 0f, 0.2f);                      // where it lay
        }
    }
}
