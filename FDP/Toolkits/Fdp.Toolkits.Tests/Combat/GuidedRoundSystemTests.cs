using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ M4 (D-D) — <see cref="GuidedRoundSystem"/> steers a guided round on the Muscle. The velocity is integrated here by hand
    /// (production: <c>LinearKinematicsSystem</c>). 📄 <c>docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md</c> §3, §7.
    /// </summary>
    public sealed class GuidedRoundSystemTests : IDisposable
    {
        private const float Dt = 1f / 60f;
        private readonly EntityRepository _w = new();
        private readonly GuidedRoundSystem _sys = new();

        public GuidedRoundSystemTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<BallisticProjectile>();
            _w.RegisterComponent<GuidedRound>();
            _w.RegisterComponent<PhysicsCollider>();
        }

        public void Dispose() => _w.Dispose();

        private Entity Unit(Vector3 at, float hull = 0f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            if (hull > 0f) _w.AddComponent(e, new PhysicsCollider { Radius = 2f, Height = hull });
            return e;
        }

        private Entity Round(Vector3 at, Vector3 velocity, GuidedRound g)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity { Linear = velocity });
            _w.AddComponent(e, new BallisticProjectile { PreviousPosition = at });
            g.SpeedMps = velocity.Length();
            _w.AddComponent(e, g);
            return e;
        }

        private void Step(Entity round)
        {
            _sys.Execute(_w, Dt);
            ref var tf = ref _w.GetComponentRW<SimTransform>(round);
            tf.Position += _w.GetComponentRO<SimVelocity>(round).Linear * Dt;
        }

        /// <summary>The distance of <paramref name="p"/> from the line <paramref name="a"/> → <paramref name="b"/>.</summary>
        private static float FromLine(Vector3 p, Vector3 a, Vector3 b)
        {
            var d = Vector3.Normalize(b - a);
            var v = p - a;
            return (v - d * Vector3.Dot(v, d)).Length();
        }

        /// <summary>
        /// ⭐⭐ M4 — a COMMAND round launched straight down the range closes onto the gunner's sight line to a target 60 m off it
        /// (TOW-like, 200 m/s, ≤ 30°/s), and stays on it; with the sight line BLOCKED (a wall between the gunner and the target) it is
        /// <see cref="GuidedPhase.Lost"/> and flies on straight — break the sight and the missile goes stupid.
        /// </summary>
        [Fact]
        public void M4_ACommandRound_FollowsTheTarget_WhileTheGunnerSeesIt_AndGoesStraightWhenSightBreaks()
        {
            var gunner = Unit(Vector3.Zero);
            var target = Unit(new Vector3(600, 90, 0));
            var guided = new GuidedRound { Guidance = GuidanceKind.Command, Target = target, Gunner = gunner };
            var round = Round(new Vector3(5, 0, 1.6f), new Vector3(200, 0, 0), guided);
            var eye = new Vector3(0, 0, 1.6f);
            var aim = AimPoint.Middle(_w, target, out _, out _);
            for (int f = 0; f < 120; f++) Step(round);   // 2 s
            var pos = _w.GetComponentRO<SimTransform>(round).Position;
            Assert.True(FromLine(pos, eye, aim) < 2f, $"2 s in, {FromLine(pos, eye, aim):0.0} m off the sight line at {pos}");
            Assert.Equal(GuidedPhase.Cruise, _w.GetComponentRO<GuidedRound>(round).Phase);

            // the same round with a wall between the gunner and the target: lost at once, straight on
            _w.SetSingletonManaged(TerrainWorldParser.Parse("""
            { "type": "FeatureCollection", "features": [
              { "type": "Feature", "properties": { "kind": "wall", "height": 6, "thickness": 0.5 }, "geometry": { "type": "LineString", "coordinates": [[300,-200],[300,300]] } } ] }
            """));
            var blind = Round(new Vector3(5, 0, 1.6f), new Vector3(200, 0, 0), guided);
            for (int f = 0; f < 60; f++) Step(blind);
            Assert.Equal(GuidedPhase.Lost, _w.GetComponentRO<GuidedRound>(blind).Phase);
            Assert.Equal(new Vector3(200, 0, 0), _w.GetComponentRO<SimVelocity>(blind).Linear);
        }

        /// <summary>
        /// ⭐⭐ M4 — a FIRE-AND-FORGET top-attack round (Javelin-like: 140 m/s, cruise 150 m, dive 60°) at a hull 1200 m away climbs to
        /// its cruise height, cruises, and dives: it reaches the hull descending at least 45°, so the face it meets is the ROOF
        /// (Armour Hit Model D-A — nothing marks a hit "top attack"; the arrival angle does).
        /// </summary>
        [Fact]
        public void M4_AJavelin_ClimbsAndDives_AndArrivesOnTheRoof()
        {
            var tank = Unit(new Vector3(1200, 0, 0), hull: 2.4f);
            var round = Round(new Vector3(0, 0, 1), new Vector3(140, 0, 0), new GuidedRound
            {
                Guidance = GuidanceKind.FireAndForget, Target = tank, Gunner = Unit(Vector3.Zero),
                TopAttack = 1, Phase = GuidedPhase.Climb, CruiseZ = 151, DiveAngleDeg = 60,
                LastSeen = new Vector3(1200, 0, 1.2f),
            });
            var middle = AimPoint.Middle(_w, tank, out _, out _);
            float maxZ = 0f, closest = float.MaxValue;
            Vector3 arrival = default;
            for (int f = 0; f < 60 * 30; f++)
            {
                Step(round);
                var p = _w.GetComponentRO<SimTransform>(round).Position;
                maxZ = MathF.Max(maxZ, p.Z);
                float d = Vector3.Distance(p, middle);
                if (d < closest) { closest = d; arrival = _w.GetComponentRO<SimVelocity>(round).Linear; }
                if (d < 2f || p.Z < 0f) break;
            }
            Assert.True(maxZ >= 145f, $"climbed only to {maxZ:0} m");
            Assert.True(closest < 3f, $"missed by {closest:0.0} m");
            float descent = MathF.Asin(-arrival.Z / arrival.Length()) * 180f / MathF.PI;
            Assert.True(descent >= 45f, $"arrived {descent:0} deg down");
            Assert.Equal(Facing.Top, ArmorModel.FacingOfArrival(_w.GetComponentRO<SimTransform>(tank), arrival));
        }

        /// <summary>⭐ M4 — the steering limit: one frame turns the velocity at most 30°/s × dt toward the aim.</summary>
        [Fact]
        public void M4_TheTurn_IsLimitedTo30DegreesASecond()
        {
            var turned = GuidedRoundSystem.Turn(new Vector3(1, 0, 0), new Vector3(0, 1, 0), 30f * MathF.PI / 180f);
            Assert.Equal(30f, MathF.Acos(Vector3.Dot(turned, Vector3.UnitX)) * 180f / MathF.PI, 2);
        }
    }
}
