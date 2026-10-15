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
        private const long Heli = 9408;
        private const long RocketPlane = 9409;
        private const long GunPlane = 9410;
        private const float Dt = 1f / 60f;
        /// <summary>An A-10's turn radius (cruise² / g·tan 60° at 150 m/s, Air Operations §6a).</summary>
        private const float TurnRadius = 1300f;
        private readonly EntityRepository _w = new();

        public AirAttackNodesTests()
        {
            SimHostComponentRegistry.RegisterAll(_w);
            CognitiveComponentRegistry.RegisterAll(_w);
            if (!_w.Bus.IsRegistered<WeaponFireIntent>()) _w.Bus.Register<WeaponFireIntent>();
            if (!_w.IsComponentTypeRegistered<WeaponMountInfo>()) _w.RegisterComponent<WeaponMountInfo>();
            if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>())
                _w.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("attack-plane", Plane);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb, AmmoGuid = 6022, InitialAmmunition = 4, DamagePerHit = 1200 } } });
            db.Register(t);
            // ⭐ slice 6 — an attack helicopter (the AH-64's suite: a chin gun on a turret, a fixed rocket pod), a rocket plane, a gun plane
            var h = new Fdp.Interfaces.TkbTemplate("attack-heli", Heli);
            h.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun, TurretIndex = 0,
                    Range = 1000f, InitialAmmunition = 4000, DamagePerHit = 30 },
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Rocket, FixedConeDeg = 8f,
                    Range = 4000f, InitialAmmunition = 38, DamagePerHit = 400 },
            } });
            db.Register(h);
            var r = new Fdp.Interfaces.TkbTemplate("rocket-plane", RocketPlane);
            r.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Rocket, FixedConeDeg = 8f, Range = 4000f, InitialAmmunition = 19, DamagePerHit = 400 } } });
            db.Register(r);
            var g = new Fdp.Interfaces.TkbTemplate("gun-plane", GunPlane);
            g.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Autocannon, FixedConeDeg = 5f, Range = 1200f, InitialAmmunition = 1100, DamagePerHit = 30 } } });
            db.Register(g);
            _w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
        }

        private Entity SpawnPlane(Vector3 at, long type = Plane, int ammo = 4,
            Fdp.Toolkit.Tkb.Domain.LocomotionClass locomotion = Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity());
            _w.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = type });
            _w.AddComponent(e, new Fdp.Toolkit.Motion.Locomotion { Class = locomotion });
            _w.AddComponent(e, new LocomotionChannel());
            _w.AddComponent(e, new WeaponChannel());
            _w.AddComponent(e, new WeaponState { Ammo = ammo });
            _w.AddComponent(e, new Fdp.Toolkit.Motion.FlightParams { TurnRadiusM = TurnRadius });
            return e;
        }

        /// <summary>The helicopter: its gun on the owner (mount 0), its rocket pod a mount child (mount 1), as CombatTkbTranslator spawns it.</summary>
        private Entity SpawnHeli(Vector3 at, out Entity pod)
        {
            var e = SpawnPlane(at, Heli, 4000, Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft);
            pod = _w.CreateEntity();
            _w.AddComponent(pod, new WeaponMountInfo { MountIndex = 1 });
            _w.AddComponent(pod, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = e, InstanceId = 1 });
            _w.AddComponent(pod, new WeaponState { Ammo = 38 });
            return e;
        }

        /// <summary>One frame of the attack with the real weapon executor (entered once per action instance, cooldowns drained, as
        /// WeaponDispatcherSystem does); returns the frame's rounds as (mount, horizontal distance to the point).</summary>
        private NodeStatus Step(ref AirAttackParams p, ref AirAttackState ws, Entity aircraft, FireAtPointExecutor executor, ref uint entered,
            System.Collections.Generic.List<(int Mount, float Distance)> shots, params Entity[] mounts)
        {
            var status = AirAttackNodes.Run(ref p, ref ws, aircraft, _w);
            ref var wc = ref _w.GetComponentRW<WeaponChannel>(aircraft);
            if (wc.ActiveAction == CombatConstants.ActionIdFireAtPoint)
            {
                if (wc.ActionInstanceId != entered) { executor.OnEnter(aircraft, ref wc, _w); entered = wc.ActionInstanceId; }
                executor.Execute(aircraft, ref wc, _w, Dt);
            }
            foreach (var m in mounts)
            {
                ref var w = ref _w.GetComponentRW<WeaponState>(m);
                w.CooldownSecondsRemaining = MathF.Max(0f, w.CooldownSecondsRemaining - Dt);
            }
            _w.Bus.SwapBuffers();
            var at = _w.GetComponentRO<SimTransform>(aircraft).Position;
            foreach (ref readonly var i in _w.Bus.Read<WeaponFireIntent>())
                shots.Add((i.WeaponIndex, new Vector2(i.TargetPoint.X - at.X, i.TargetPoint.Y - at.Y).Length()));
            return status;
        }

        /// <summary>The stand-in flight: straight at the MoveTo at <paramref name="speed"/>, the nose along the track (level), ARRIVED within the
        /// arrival radius (a helicopter then hovers) or — a plane — within its turn radius, and then it circles.</summary>
        private void FlyStandIn(Entity e, float speed, bool plane)
        {
            var move = MoveOf(e);
            ref var tr = ref _w.GetComponentRW<SimTransform>(e);
            var to = move.Destination - tr.Position;
            var flat = new Vector2(to.X, to.Y);
            Vector3 v;
            if (flat.Length() <= (plane ? MathF.Max(move.ArrivalRadius, TurnRadius) : MathF.Max(move.ArrivalRadius, 5f)))
            {
                _w.GetComponentRW<LocomotionChannel>(e).Status = NodeStatus.Success;
                if (plane)
                {
                    var heading = _w.GetComponentRO<SimVelocity>(e).Linear;
                    var h = new Vector2(heading.X, heading.Y);
                    h = h.LengthSquared() > 1e-3f ? Vector2.Normalize(h) : new Vector2(1f, 0f);
                    float turn = speed / TurnRadius * Dt;
                    h = new Vector2(h.X * MathF.Cos(turn) - h.Y * MathF.Sin(turn), h.X * MathF.Sin(turn) + h.Y * MathF.Cos(turn));
                    v = new Vector3(h * speed, 0f);
                }
                else v = Vector3.Zero;
            }
            else
            {
                _w.GetComponentRW<LocomotionChannel>(e).Status = NodeStatus.Running;
                v = Vector3.Normalize(new Vector3(flat, 0f)) * speed;
                if (MathF.Abs(to.Z) > 0.5f) v.Z = MathF.Sign(to.Z) * MathF.Min(MathF.Abs(to.Z) / Dt, 10f);
            }
            _w.GetComponentRW<SimVelocity>(e).Linear = v;
            tr.Position += v * Dt;
            if (v.X * v.X + v.Y * v.Y > 1e-3f)
                tr.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Atan2(v.Y, v.X));   // level, nose on the track
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

        /// <summary>⭐ The profile (Air Operations §7's table): a plane with a bomb ⇒ LevelBomb (that mount); a plane with a rocket pod ⇒
        /// RocketRun; a helicopter with rockets and a gun ⇒ RunningFire (the pod and the gun). A helicopter with only a bomb, or a plane with
        /// only a gun (GunRun, not built), has no profile ⇒ the attack fails at once (loudly) instead of flying somewhere and doing nothing;
        /// so does a GIVEN profile the aircraft has no weapon for.</summary>
        [Fact]
        public void B4_TheProfile_IsPickedFromTheAircraftAndItsWeapons()
        {
            var plane = SpawnPlane(Vector3.Zero);
            Assert.Equal(AirAttackProfile.LevelBomb, AirAttackProfiles.Pick(_w, plane, out byte mount));
            Assert.Equal(0, mount);

            var bombHeli = SpawnPlane(Vector3.Zero, locomotion: Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft);
            Assert.Null(AirAttackProfiles.Pick(_w, bombHeli, out _));
            var p = new AirAttackParams { Point = new Vector3(1000f, 0f, 0f) };
            var ws = default(AirAttackState);
            Assert.Equal(NodeStatus.Failure, AirAttackNodes.Run(ref p, ref ws, bombHeli, _w));

            var heli = SpawnHeli(Vector3.Zero, out _);
            Assert.Equal(AirAttackProfile.RunningFire, AirAttackProfiles.Pick(_w, heli, out mount, out byte gun));
            Assert.Equal(1, mount);
            Assert.Equal(0, gun);

            var rocketPlane = SpawnPlane(Vector3.Zero, RocketPlane, 19);
            Assert.Equal(AirAttackProfile.RocketRun, AirAttackProfiles.Pick(_w, rocketPlane, out mount, out gun));
            Assert.Equal(0, mount);
            Assert.Equal(AirAttackProfiles.None, gun);
            p = new AirAttackParams { Point = new Vector3(1000f, 0f, 0f), Profile = AirAttackProfile.LevelBomb };
            ws = default;
            Assert.Equal(NodeStatus.Failure, AirAttackNodes.Run(ref p, ref ws, rocketPlane, _w));   // no bomb for a bomb run

            Assert.Null(AirAttackProfiles.Pick(_w, SpawnPlane(Vector3.Zero, GunPlane, 1100), out _));
        }

        /// <summary>
        /// ⭐⭐ Slice 6 — RunningFire: a helicopter runs in low (60 m above the point) from 4 km, ripples its 8 rockets inside the window
        /// (2.5 km down to the 400 m break), then its gun from 900 m (0.9 × its range), and at 400 m it breaks away to the right — it is
        /// never nearer the point than the break; the attack succeeds when it reaches the break-away point.
        /// ✅ Red-proofs: no break (the run-in kept) ⇒ the helicopter flies over the point ⇒ red on the closest pass; the window's far edge
        /// at the pod's 4 km range instead of the fire range ⇒ rockets beyond 2.5 km ⇒ red; the gun not started after the rockets ⇒ red.
        /// </summary>
        [Fact]
        public void S6_ARunningFire_RipplesTheRocketsThenTheGun_AndBreaksOffShort()
        {
            var heli = SpawnHeli(new Vector3(-4000f, 300f, 60f), out var pod);
            var target = Vector3.Zero;
            var p = new AirAttackParams { Point = target };
            var ws = default(AirAttackState);
            var executor = new FireAtPointExecutor();
            var shots = new System.Collections.Generic.List<(int Mount, float Distance)>();
            uint entered = 0;
            float closest = float.MaxValue, heightAtFire = 0f;
            var status = NodeStatus.Running;
            for (int frame = 0; frame < 60 * 180 && status == NodeStatus.Running; frame++)
            {
                int before = shots.Count;
                status = Step(ref p, ref ws, heli, executor, ref entered, shots, heli, pod);
                var at = _w.GetComponentRO<SimTransform>(heli).Position;
                if (shots.Count > before && before == 0) heightAtFire = at.Z;
                closest = MathF.Min(closest, new Vector2(at.X, at.Y).Length());
                FlyStandIn(heli, 60f, plane: false);
            }

            var rockets = shots.FindAll(s => s.Mount == 1);
            var gun = shots.FindAll(s => s.Mount == 0);
            Assert.Equal(AirAttackNodes.DefaultRipple, rockets.Count);
            Assert.All(rockets, s => Assert.InRange(s.Distance, AirAttackNodes.DefaultBreakM, AirAttackNodes.DefaultRocketFireM));
            Assert.Equal(38 - AirAttackNodes.DefaultRipple, _w.GetComponentRO<WeaponState>(pod).Ammo);
            Assert.Equal(AirAttackNodes.GunBurst, gun.Count);
            Assert.All(gun, s => Assert.InRange(s.Distance, AirAttackNodes.DefaultBreakM, 900f));
            Assert.True(shots.FindLastIndex(s => s.Mount == 1) < shots.FindIndex(s => s.Mount == 0), "the rockets first, then the gun");
            Assert.InRange(heightAtFire, 60f - 5f, 60f + 5f);
            Assert.True(closest >= AirAttackNodes.DefaultBreakM - 5f, $"closest {closest} m — it must break off at the 400 m break");
            Assert.Equal(NodeStatus.Success, status);
        }

        /// <summary>
        /// ⭐ Slice 6 — RocketRun: a plane with a rocket pod (fixed, 8° cone) flies a low pass (150 m) from 9 km out, off the line, ripples
        /// its 8 rockets inside the window (2.5 km down to its 800 m pull-out) and flies through and out; the attack succeeds on arrival.
        /// ✅ Red-proofs: the run flown at the bomb run's 300 m ⇒ red on the pass height; the window's far edge at the pod's 4 km range
        /// ⇒ rockets beyond 2.5 km ⇒ red.
        /// </summary>
        [Fact]
        public void S6_ARocketRun_RipplesInsideTheWindow_AndFliesThrough()
        {
            var plane = SpawnPlane(new Vector3(-9000f, 1200f, 500f), RocketPlane, 19);
            var p = new AirAttackParams { Point = Vector3.Zero };
            var ws = default(AirAttackState);
            var executor = new FireAtPointExecutor();
            var shots = new System.Collections.Generic.List<(int Mount, float Distance)>();
            uint entered = 0;
            float closest = float.MaxValue, heightAtFire = 0f;
            var status = NodeStatus.Running;
            for (int frame = 0; frame < 60 * 180 && status == NodeStatus.Running; frame++)
            {
                int before = shots.Count;
                status = Step(ref p, ref ws, plane, executor, ref entered, shots, plane);
                var at = _w.GetComponentRO<SimTransform>(plane).Position;
                if (shots.Count > before && before == 0) heightAtFire = at.Z;
                closest = MathF.Min(closest, new Vector2(at.X, at.Y).Length());
                FlyStandIn(plane, 150f, plane: true);
            }

            Assert.Equal(AirAttackNodes.DefaultRipple, shots.Count);
            Assert.All(shots, s => Assert.InRange(s.Distance, AirAttackNodes.DefaultPlaneBreakM, AirAttackNodes.DefaultRocketFireM));
            Assert.Equal(19 - AirAttackNodes.DefaultRipple, _w.GetComponentRO<WeaponState>(plane).Ammo);
            Assert.InRange(heightAtFire, AirAttackNodes.DefaultRocketRunHeightAgl - 15f, AirAttackNodes.DefaultRocketRunHeightAgl + 15f);
            Assert.True(closest < 200f, $"closest {closest} m — a plane flies through over the point");
            Assert.Equal(NodeStatus.Success, status);
        }
    }
}
