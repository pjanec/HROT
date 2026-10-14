using System;
using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Navigation;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 5 (B4, B6, A6; 📄 docs/designs/air-attack/DESIGN_Air_Operations.md §9c) — the ONE air attack: a plane with a
    /// bomb flies a level pass over the point and its weapon brain releases on the cue. The plane is flown here by a stand-in (straight at
    /// the MoveTo it was given, at a fixed speed) and the weapon by the real <see cref="FireAtPointExecutor"/>, so the rail proves the
    /// behaviour's geometry and the release, not the flight model (backend's <c>FixedWingModelTests</c>).
    /// </summary>
    public class AirAttackNodesTests
    {
        private const long Plane = 9407;
        private const float Dt = 1f / 60f;
        /// <summary>An A-10's turn radius (cruise² / g·tan 60° at 150 m/s, Air Operations §6a).</summary>
        private const float TurnRadius = 1300f;
        private readonly EntityRepository _w = new();

        public AirAttackNodesTests()
        {
            SimHostComponentRegistry.RegisterAll(_w);
            CognitiveComponentRegistry.RegisterAll(_w);
            if (!_w.Bus.IsRegistered<WeaponFireIntent>()) _w.Bus.Register<WeaponFireIntent>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("attack-plane", Plane);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb, AmmoGuid = 6022, InitialAmmunition = 4, DamagePerHit = 1200 } } });
            db.Register(t);
            _w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
        }

        private Entity SpawnPlane(Vector3 at)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity());
            _w.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = Plane });
            _w.AddComponent(e, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing });
            _w.AddComponent(e, new LocomotionChannel());
            _w.AddComponent(e, new WeaponChannel());
            _w.AddComponent(e, new WeaponState { Ammo = 4 });
            _w.AddComponent(e, new Fdp.Toolkit.Motion.FlightParams { TurnRadiusM = TurnRadius });
            return e;
        }

        private unsafe MoveToParams MoveOf(Entity e)
        {
            var ch = _w.GetComponentRO<LocomotionChannel>(e);
            return *(MoveToParams*)ch.Params;
        }

        /// <summary>
        /// ⭐ LevelBomb, flown from 9 km out, off the line: the node steers at a point BEYOND the target on the line through it (never at the
        /// target, whose arrival circle would turn the plane before the release) at the pass height; the weapon holds the bomb until the
        /// cue and drops ONE where it lands on the point (within 15 m); then the plane flies on out and the attack succeeds on arrival.
        /// The stand-in flies a point as <c>FixedWingModel.ToPoint</c> does: arrived within the turn radius, then it circles.
        /// ✅ Red-proofs: steering AT the target ⇒ the plane starts circling 1.3 km out, before the release ⇒ no bomb on the point ⇒ red;
        /// the FireAtPoint release gate off ⇒ the bomb leaves at once, kilometres short ⇒ red.
        /// </summary>
        [Fact]
        public void B6_ALevelBombRun_HitsThePoint()
        {
            var plane = SpawnPlane(new Vector3(-9000f, 1200f, 500f));
            var target = new Vector3(0f, 0f, 0f);
            var p = new AirAttackParams { Point = target };
            var ws = default(AirAttackState);
            var executor = new FireAtPointExecutor();
            const float Speed = 150f;
            uint entered = 0;
            int released = 0;
            Vector3 releaseAt = default, releaseVel = default;
            NodeStatus status = NodeStatus.Running;

            for (int frame = 0; frame < 60 * 120 && status == NodeStatus.Running; frame++)
            {
                status = AirAttackNodes.Run(ref p, ref ws, plane, _w);
                // the weapon: the real executor, entered once per action instance (as WeaponDispatcherSystem does)
                ref var wc = ref _w.GetComponentRW<WeaponChannel>(plane);
                if (wc.ActiveAction == CombatConstants.ActionIdFireAtPoint)
                {
                    if (wc.ActionInstanceId != entered) { executor.OnEnter(plane, ref wc, _w); entered = wc.ActionInstanceId; }
                    executor.Execute(plane, ref wc, _w, Dt);
                }
                _w.Bus.SwapBuffers();
                if (_w.Bus.Read<WeaponFireIntent>().Length > 0 && released++ == 0)
                {
                    releaseAt = _w.GetComponentRO<SimTransform>(plane).Position;
                    releaseVel = _w.GetComponentRO<SimVelocity>(plane).Linear;
                }
                // the stand-in flight, as FixedWingModel.ToPoint flies a point: straight at it, and ARRIVED within
                // max(arrival radius, turn radius) — then it circles (a plane never stops)
                var move = MoveOf(plane);
                ref var tr = ref _w.GetComponentRW<SimTransform>(plane);
                var to = move.Destination - tr.Position;
                var flat = new Vector2(to.X, to.Y);
                Vector3 v;
                if (flat.Length() <= MathF.Max(move.ArrivalRadius, TurnRadius))
                {
                    _w.GetComponentRW<LocomotionChannel>(plane).Status = NodeStatus.Success;
                    var heading = _w.GetComponentRO<SimVelocity>(plane).Linear;
                    var h = new Vector2(heading.X, heading.Y);
                    h = h.LengthSquared() > 1e-3f ? Vector2.Normalize(h) : new Vector2(1f, 0f);
                    float turn = Speed / TurnRadius * Dt;                                   // circle left
                    h = new Vector2(h.X * MathF.Cos(turn) - h.Y * MathF.Sin(turn), h.X * MathF.Sin(turn) + h.Y * MathF.Cos(turn));
                    v = new Vector3(h * Speed, 0f);
                }
                else
                {
                    v = Vector3.Normalize(new Vector3(flat, 0f)) * Speed;
                    if (MathF.Abs(to.Z) > 0.5f) v.Z = MathF.Sign(to.Z) * MathF.Min(MathF.Abs(to.Z) / Dt, 10f);
                }
                _w.GetComponentRW<SimVelocity>(plane).Linear = v;
                tr.Position += v * Dt;
            }

            Assert.Equal(1, released);
            Assert.Equal(3, _w.GetComponentRO<WeaponState>(plane).Ammo);
            Assert.InRange(releaseAt.Z, 300f - 15f, 300f + 15f);                                       // the pass height above the point
            var (impact, _) = BombBallistics.Impact(releaseAt, BombBallistics.Launch(releaseVel), null, Dt);
            Assert.True(new Vector2(impact.X - target.X, impact.Y - target.Y).Length() <= 15f, $"impact {impact} vs {target}");
            Assert.Equal(NodeStatus.Success, status);                                                   // flown out
        }

        /// <summary>⭐ The profile: a plane with a bomb mount ⇒ LevelBomb (that mount); a plane without one, or a helicopter, has no profile
        /// yet ⇒ the attack fails at once (loudly) instead of flying somewhere and doing nothing.</summary>
        [Fact]
        public void B4_TheProfile_IsPickedFromTheAircraftAndItsWeapons()
        {
            var plane = SpawnPlane(Vector3.Zero);
            Assert.Equal(AirAttackProfile.LevelBomb, AirAttackProfiles.Pick(_w, plane, out byte mount));
            Assert.Equal(0, mount);

            var heli = SpawnPlane(Vector3.Zero);
            _w.GetComponentRW<Fdp.Toolkit.Motion.Locomotion>(heli).Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft;
            Assert.Null(AirAttackProfiles.Pick(_w, heli, out _));
            var p = new AirAttackParams { Point = new Vector3(1000f, 0f, 0f) };
            var ws = default(AirAttackState);
            Assert.Equal(NodeStatus.Failure, AirAttackNodes.Run(ref p, ref ws, heli, _w));
        }
    }
}
