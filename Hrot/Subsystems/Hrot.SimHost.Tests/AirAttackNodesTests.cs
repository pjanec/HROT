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
        /// <summary>⭐ Slice 6c — a plane with all three: the bomb rack (mount 0), a rocket pod (1), a fixed gun (2).</summary>
        private const long MultiPlane = 9411;
        private const float GunMuzzle = 1010f;
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
                { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Autocannon, FixedConeDeg = 5f, Range = 1200f, InitialAmmunition = 1100, DamagePerHit = 30,
                  MuzzleVelocity = GunMuzzle } } });
            db.Register(g);
            var m = new Fdp.Interfaces.TkbTemplate("multi-plane", MultiPlane);
            m.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            {
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb, AmmoGuid = 6022, InitialAmmunition = 4 },
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Rocket, FixedConeDeg = 8f, Range = 4000f },
                new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Autocannon, FixedConeDeg = 5f, Range = 1200f },
            } });
            db.Register(m);
            // the bomb's round: a Mk-82-like burst, 27 m injury radius (W3 reads it from the warhead)
            var bombRound = new Fdp.Interfaces.TkbTemplate("test bomb", 6022);
            bombRound.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WarheadDto { BlastInjuryRadiusM = 27f, BlastLethalRadiusM = 9f });
            db.Register(bombRound);
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

        /// <summary>⭐ <c>Q94 A-3</c> (R-3018) — the stand-in muscle: every round a weapon ASKED is answered FIRED at once
        /// (<see cref="ShotLedger.Answer"/>), so the rounds the asserts count are spent as the real muscle's notifications would spend them.</summary>
        private void AnswerFired()
        {
            foreach (var e in _w.Query().With<WeaponState>().Build())
            {
                ref var w = ref _w.GetComponentRW<WeaponState>(e);
                while (ShotLedger.Pending(w) > 0) ShotLedger.Answer(_w, e, ref w, unchecked(w.LastAnsweredId + 1), FireRefusal.None);
            }
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
                AnswerFired();   // ⭐ Q94 A-3 — the muscle answers every ask "fired" (no FireProcessingSystem in this harness)
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
                    AnswerFired();   // ⭐ Q94 A-3 — the muscle answers every ask "fired" (no FireProcessingSystem in this harness)
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

            // ⭐ slice 6c — a plane with only a gun now flies a GunRun (SUPERSEDES "no profile": GunRun is built)
            Assert.Equal(AirAttackProfile.GunRun, AirAttackProfiles.Pick(_w, SpawnPlane(Vector3.Zero, GunPlane, 1100), out mount, out gun));
            Assert.Equal(AirAttackProfiles.None, mount);
            Assert.Equal(0, gun);
        }

        /// <summary>
        /// ⭐⭐ Slice 6c (B12, W1; Air Operations §9e) — the weapon follows the target: an ENTITY draws the plane's gun first (GunRun), then
        /// rockets, the bomb last; a POINT draws the bomb first; a heavy point (a building) never the gun — with the bomb and the rockets gone
        /// it has no profile at all. A helicopter flies RunningFire either way. ✅ Red-proofs: the point order for an entity ⇒ LevelBomb ⇒ red;
        /// the gun allowed on a heavy point ⇒ GunRun ⇒ red.
        /// </summary>
        [Fact]
        public void S6c_TheWeaponFollowsTheTarget_DirectFireAtAnEntity_TheBombAtAPoint_NeverTheGunOnAHeavyPoint()
        {
            var plane = SpawnPlane(Vector3.Zero, MultiPlane);
            Assert.Equal(AirAttackProfile.GunRun,
                AirAttackProfiles.Pick(_w, plane, out _, out byte gun, entity: true, Hrot.AI.Behaviors.Orders.StrikeTargetKind.Unspecified, 0));
            Assert.Equal(2, gun);
            Assert.Equal(AirAttackProfile.RocketRun,
                AirAttackProfiles.Pick(_w, plane, out byte mount, out _, entity: true, Hrot.AI.Behaviors.Orders.StrikeTargetKind.Unspecified, 1 << 2));
            Assert.Equal(1, mount);
            Assert.Equal(AirAttackProfile.LevelBomb,
                AirAttackProfiles.Pick(_w, plane, out mount, out _, entity: true, Hrot.AI.Behaviors.Orders.StrikeTargetKind.Unspecified, (1 << 2) | (1 << 1)));
            Assert.Equal(0, mount);

            Assert.Equal(AirAttackProfile.LevelBomb, AirAttackProfiles.Pick(_w, plane, out mount, out _));
            Assert.Equal(AirAttackProfile.RocketRun,
                AirAttackProfiles.Pick(_w, plane, out _, out _, entity: false, Hrot.AI.Behaviors.Orders.StrikeTargetKind.Building, 1 << 0));
            Assert.Null(AirAttackProfiles.Pick(_w, plane, out _, out _, entity: false, Hrot.AI.Behaviors.Orders.StrikeTargetKind.Building, (1 << 0) | (1 << 1)));
            Assert.Equal(AirAttackProfile.GunRun,
                AirAttackProfiles.Pick(_w, plane, out _, out _, entity: false, Hrot.AI.Behaviors.Orders.StrikeTargetKind.Personnel, (1 << 0) | (1 << 1)));

            var heli = SpawnHeli(Vector3.Zero, out _);
            Assert.Equal(AirAttackProfile.RunningFire,
                AirAttackProfiles.Pick(_w, heli, out _, out _, entity: true, Hrot.AI.Behaviors.Orders.StrikeTargetKind.Unspecified, 0));
        }

        /// <summary>
        /// ⭐⭐ Slice 6c (W3) — a weapon whose burst would reach a friend or a NEUTRAL near the point is passed over: the bomb (27 m injury)
        /// with a friend 15 m from the point ⇒ the strike takes the next weapon (rockets, then the gun — neither has a burst here); a neutral
        /// does the same; DANGER CLOSE fires the bomb anyway; and on a building (no gun) with the bomb and rockets out of reach the attack
        /// fails loudly. ✅ Red-proof: the check skipped ⇒ LevelBomb with a friend inside the burst ⇒ red.
        /// </summary>
        [Fact]
        public void S6c_AWeaponWhoseBurstReachesAFriendOrANeutral_IsPassedOver_UnlessDangerClose()
        {
            var plane = SpawnPlane(new Vector3(-8000f, 0f, 300f), MultiPlane);
            _w.AddComponent(plane, new EntityInfo { ForceId = ForceId.Friend });
            var man = _w.CreateEntity();
            _w.AddComponent(man, new SimTransform { Position = new Vector3(15f, 0f, 0f), Rotation = Quaternion.Identity });
            _w.AddComponent(man, new EntityInfo { ForceId = ForceId.Friend });
            _w.AddComponent(man, new Health { Current = 100f, Max = 100f });

            AirAttackProfile Picked(Hrot.AI.Behaviors.Orders.StrikeTargetKind kind, byte dangerClose, out NodeStatus status)
            {
                var p = new AirAttackParams { Point = Vector3.Zero, Kind = kind, DangerClose = dangerClose };
                var ws = default(AirAttackState);
                status = AirAttackNodes.Run(ref p, ref ws, plane, _w);
                return ws.Profile;
            }

            Assert.Equal(AirAttackProfile.RocketRun, Picked(Hrot.AI.Behaviors.Orders.StrikeTargetKind.Unspecified, 0, out var s1));
            Assert.Equal(NodeStatus.Running, s1);
            Assert.Equal(AirAttackProfile.LevelBomb, Picked(Hrot.AI.Behaviors.Orders.StrikeTargetKind.Unspecified, 1, out _));
            _w.GetComponentRW<EntityInfo>(man).ForceId = ForceId.Neutral;
            Assert.Equal(AirAttackProfile.RocketRun, Picked(Hrot.AI.Behaviors.Orders.StrikeTargetKind.Unspecified, 0, out _));
            _w.GetComponentRW<EntityInfo>(man).ForceId = ForceId.Hostile;
            Assert.Equal(AirAttackProfile.LevelBomb, Picked(Hrot.AI.Behaviors.Orders.StrikeTargetKind.Unspecified, 0, out _));
        }

        /// <summary>
        /// ⭐⭐⭐ Slice 6c (B12–B14) — Attack on a MOVING entity: a plane with only a gun flies a GunRun at a technical driving across its path
        /// at 5 m/s. Every round is aimed at the LED point — a round flown from the muzzle at its own speed plus the plane's velocity (A4) passes
        /// within 1.5 m of the technical; the technical survives the first pass, so the plane comes round for a second; killed during it, the
        /// attack ends Success. ✅ Red-proofs: aiming at where the technical is ⇒ the rounds pass ~4 m behind ⇒ red; one pass only ⇒ no second
        /// pass ⇒ red; no "destroyed ⇒ Success" ⇒ the attack runs on ⇒ red; a burst cut short by the break not counted as fired ⇒ the plane
        /// turns back 1 km past the target as if it had fired nothing ⇒ red.
        /// </summary>
        [Fact]
        public void S6c_AGunRunOnAMovingTechnical_LeadsIt_ComesRound_AndEndsWhenItIsDestroyed()
        {
            var plane = SpawnPlane(new Vector3(-7000f, 0f, 60f), GunPlane, 1100);
            var tech = _w.CreateEntity();
            var techV = new Vector3(0f, 5f, 0f);
            _w.AddComponent(tech, new SimTransform { Position = new Vector3(0f, -150f, 0f), Rotation = Quaternion.Identity });
            _w.AddComponent(tech, new SimVelocity { Linear = techV });
            _w.AddComponent(tech, new Health { Current = 100f, Max = 100f });
            var p = new AirAttackParams { Target = tech, Point = _w.GetComponentRO<SimTransform>(tech).Position };
            var ws = default(AirAttackState);
            var executor = new FireAtPointExecutor();
            uint entered = 0;
            int shots = 0, shotsPass1 = 0;
            float worstMiss = 0f, farthestOut = 0f;
            var status = NodeStatus.Running;
            for (int frame = 0; frame < 60 * 400 && status == NodeStatus.Running; frame++)
            {
                _w.SetSingleton(new GlobalTime { TotalTime = frame * Dt });   // the run-in re-aims on sim time (every 0.5 s)
                status = AirAttackNodes.Run(ref p, ref ws, plane, _w);
                ref var wc = ref _w.GetComponentRW<WeaponChannel>(plane);
                if (wc.ActiveAction == CombatConstants.ActionIdFireAtPoint)
                {
                    if (wc.ActionInstanceId != entered) { executor.OnEnter(plane, ref wc, _w); entered = wc.ActionInstanceId; }
                    executor.Execute(plane, ref wc, _w, Dt);
                    AnswerFired();   // ⭐ Q94 A-3 — the muscle answers every ask "fired" (no FireProcessingSystem in this harness)
                }
                ref var w = ref _w.GetComponentRW<WeaponState>(plane);
                w.CooldownSecondsRemaining = MathF.Max(0f, w.CooldownSecondsRemaining - Dt);
                _w.Bus.SwapBuffers();
                var at = _w.GetComponentRO<SimTransform>(plane).Position;
                var pv = _w.GetComponentRO<SimVelocity>(plane).Linear;
                var tp = _w.GetComponentRO<SimTransform>(tech).Position;
                foreach (ref readonly var i in _w.Bus.Read<WeaponFireIntent>())
                {
                    shots++;
                    // fly the round: from the plane at the muzzle speed along the aim plus the plane's velocity, against the moving target
                    var rv = Vector3.Normalize(i.TargetPoint - at) * GunMuzzle + pv;
                    float miss = float.MaxValue;
                    for (float t = 0f; t < 3f; t += 0.0005f) miss = MathF.Min(miss, Vector3.Distance(at + rv * t, tp + techV * t));
                    worstMiss = MathF.Max(worstMiss, miss);
                }
                if (ws.Passes == 0) { shotsPass1 = shots; farthestOut = MathF.Max(farthestOut, at.X); }
                if (ws.Passes >= 1 && shots > shotsPass1 && CombatLife.IsAlive(_w, tech))
                    _w.GetComponentRW<Health>(tech).Current = 0f;   // the second pass's rounds kill it
                FlyStandIn(plane, 150f, plane: true);
                _w.GetComponentRW<SimTransform>(tech).Position = tp + techV * Dt;
            }

            Assert.Equal(AirAttackProfile.GunRun, ws.Profile);
            Assert.True(shotsPass1 > 0, "the first pass fired");
            Assert.True(shots > shotsPass1, $"a second pass fired ({shots} rounds, {shotsPass1} in the first)");
            Assert.True(worstMiss < 1.5f, $"the worst round passed {worstMiss:F2} m from the technical — it must be led");
            // the pass that fired (its burst cut short by the 600 m break) flies OUT to the egress before it comes round — not the
            //   "overflown with nothing fired" turn-back 1 km past the target
            Assert.True(farthestOut > 2000f, $"the first pass turned back {farthestOut:F0} m past the target — it fired, so it flies out");
            Assert.Equal(NodeStatus.Success, status);
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

        /// <summary>
        /// ⭐ Slice 6c, found live (ua-air-attack ②, `2026-10-15`) — an entity attack's gun aims at the target's MIDDLE (its hull's half
        /// height, <see cref="AimPoint.Middle"/> — the height FireProcessingSystem aims at), not at its ground point: aimed at the ground in a
        /// 3° gun pass every 30 mm round burst ~7 m short of the truck. ✅ Red-proof: the middle dropped ⇒ every aim point at Z 0 ⇒ red.
        /// </summary>
        [Fact]
        public void S6c_AGunRunAimsAtTheTargetsMiddle_NotItsGroundPoint()
        {
            if (!_w.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>())
                _w.RegisterComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>();
            if (!_w.IsComponentTypeRegistered<Health>()) _w.RegisterComponent<Health>();
            var plane = SpawnPlane(new Vector3(-7000f, 0f, 50f), GunPlane, 1100);
            var truck = _w.CreateEntity();
            _w.AddComponent(truck, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _w.AddComponent(truck, new SimVelocity());
            _w.AddComponent(truck, new Health { Current = 1e6f, Max = 1e6f });
            _w.AddComponent(truck, new Fdp.Toolkit.Physics.Components.PhysicsCollider { Radius = 2.5f, Height = 2f });
            var p = new AirAttackParams { Target = truck, Point = Vector3.Zero };
            var ws = default(AirAttackState);
            var executor = new FireAtPointExecutor();
            uint entered = 0;
            var aims = new System.Collections.Generic.List<float>();
            for (int frame = 0; frame < 60 * 60 && aims.Count < 20; frame++)
            {
                _w.SetSingleton(new GlobalTime { TotalTime = frame * Dt });
                AirAttackNodes.Run(ref p, ref ws, plane, _w);
                ref var wc = ref _w.GetComponentRW<WeaponChannel>(plane);
                if (wc.ActiveAction == CombatConstants.ActionIdFireAtPoint)
                {
                    if (wc.ActionInstanceId != entered) { executor.OnEnter(plane, ref wc, _w); entered = wc.ActionInstanceId; }
                    executor.Execute(plane, ref wc, _w, Dt);
                    AnswerFired();   // ⭐ Q94 A-3 — the muscle answers every ask "fired" (no FireProcessingSystem in this harness)
                }
                ref var w = ref _w.GetComponentRW<WeaponState>(plane);
                w.CooldownSecondsRemaining = MathF.Max(0f, w.CooldownSecondsRemaining - Dt);
                _w.Bus.SwapBuffers();
                foreach (ref readonly var i in _w.Bus.Read<WeaponFireIntent>()) aims.Add(i.TargetPoint.Z);
                FlyStandIn(plane, 150f, plane: true);
            }
            Assert.True(aims.Count >= 5, $"the gun fired: {aims.Count} rounds");
            Assert.All(aims, z => Assert.InRange(z, 0.95f, 1.05f));   // the 2 m hull's middle — the plane flies level, so the lead adds no height
        }

        // ── ⭐ Slice 6d (Air Operations §9f) — DoorGunOrbit ─────────────────────────────────────────────────────────────────────────

        private const long DoorHeli = 9412, DoorGun = 9413;
        private const float DoorGunMuzzle = 850f;

        private void Reg<T>() where T : unmanaged { if (!_w.IsComponentTypeRegistered<T>()) _w.RegisterComponent<T>(); }

        /// <summary>The doors-off UH-60 as 404 has it (BdcTkbCatalog DoorSeats): no weapon of its own; per side a door gunner (seat 8 left,
        /// 10 right) and a Door station operated by him (9, 11) holding an M134, its arc 20…160° on its side. The M134s are mounts 32 / 33.</summary>
        private Entity SpawnDoorHeli(Vector3 at, out Entity leftGun, out Entity rightGun, out Entity leftGunner, out Entity rightGunner)
        {
            Reg<Fdp.Toolkit.Combat.WeaponSystemParams>();
            Reg<IsEmbarkedTag>();
            Reg<PassengerBuffer>();
            Reg<CarrierStations>();
            Reg<Health>();
            var db = _w.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() as Fdp.Toolkit.Tkb.TkbDatabase;
            if (!db!.TryGetByType(DoorGun, out _))
            {
                db.Register(new Fdp.Interfaces.TkbTemplate("door-heli", DoorHeli));
                var g = new Fdp.Interfaces.TkbTemplate("m134", DoorGun);
                g.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                    { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun, Range = 1000f, InitialAmmunition = 3000, DamagePerHit = 30,
                      MuzzleVelocity = DoorGunMuzzle } } });
                db.Register(g);
            }
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity());
            _w.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = DoorHeli });
            _w.AddComponent(e, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft });
            _w.AddComponent(e, new LocomotionChannel());
            _w.AddComponent(e, new WeaponChannel());
            _w.AddComponent(e, new Fdp.Toolkit.Motion.FlightParams { TurnRadiusM = 60f });
            var stations = new CarrierStations { StationMask = (ushort)((1 << 9) | (1 << 11)) };
            float deg = MathF.PI / 180f;
            stations.Seats[9] = new StationSeat { OperatedBy = 8, NeedsGunner = 1, HasArc = 1, TraverseMin = 20f * deg, TraverseMax = 160f * deg,
                                                  ElevationMin = -90f * deg, ElevationMax = 90f * deg };
            stations.Seats[11] = new StationSeat { OperatedBy = 10, NeedsGunner = 1, HasArc = 1, TraverseMin = -160f * deg, TraverseMax = -20f * deg,
                                                   ElevationMin = -90f * deg, ElevationMax = 90f * deg };
            _w.AddComponent(e, stations);
            Entity Rider(byte seat, bool gun)
            {
                var r = _w.CreateEntity();
                _w.AddComponent(r, new IsEmbarkedTag { VehicleEntity = e, Seat = seat, Exposed = 1 });
                if (gun)
                {
                    _w.AddComponent(r, new Fdp.Toolkit.Combat.WeaponSystemParams { NeedsOperator = 1 });
                    _w.AddComponent(r, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = DoorGun });
                    _w.AddComponent(r, new WeaponState { Ammo = 3000, MuzzleVelocity = DoorGunMuzzle });
                }
                else _w.AddComponent(r, new Health { Current = 100f, Max = 100f });
                return r;
            }
            leftGunner = Rider(8, false); leftGun = Rider(9, true); rightGunner = Rider(10, false); rightGun = Rider(11, true);
            var list = new PassengerBuffer { Count = 4 };
            list.Passengers[8] = leftGunner; list.Passengers[9] = leftGun; list.Passengers[10] = rightGunner; list.Passengers[11] = rightGun;
            _w.AddComponent(e, list);
            return e;
        }

        private Entity SpawnTechnical(Vector3 at)
        {
            var t = _w.CreateEntity();
            _w.AddComponent(t, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(t, new SimVelocity());
            _w.AddComponent(t, new Health { Current = 100f, Max = 100f });
            return t;
        }

        /// <summary>One frame of a door-gun orbit: the node, the real executor, the stand-in flight at 70 m/s; returns the frame's rounds
        /// as (the gun that fired, the closest the round — the aim's direction at the gun's speed PLUS the helicopter's velocity, A4 — passes
        /// the target).</summary>
        private NodeStatus OrbitStep(ref AirAttackParams p, ref AirAttackState ws, Entity heli, FireAtPointExecutor executor, ref uint entered,
            int frame, Vector3 target, System.Collections.Generic.List<(Entity Gun, float Miss)> shots, params Entity[] guns)
        {
            _w.SetSingleton(new GlobalTime { TotalTime = frame * Dt });
            var status = AirAttackNodes.Run(ref p, ref ws, heli, _w);
            ref var wc = ref _w.GetComponentRW<WeaponChannel>(heli);
            if (wc.ActiveAction == CombatConstants.ActionIdFireAtPoint)
            {
                if (wc.ActionInstanceId != entered) { executor.OnEnter(heli, ref wc, _w); entered = wc.ActionInstanceId; }
                executor.Execute(heli, ref wc, _w, Dt);
                AnswerFired();   // ⭐ Q94 A-3 — the muscle answers every ask "fired" (no FireProcessingSystem in this harness)
            }
            foreach (var g in guns)
            {
                ref var w = ref _w.GetComponentRW<WeaponState>(g);
                w.CooldownSecondsRemaining = MathF.Max(0f, w.CooldownSecondsRemaining - Dt);
            }
            _w.Bus.SwapBuffers();
            var at = _w.GetComponentRO<SimTransform>(heli).Position;
            var v = _w.GetComponentRO<SimVelocity>(heli).Linear;
            foreach (ref readonly var i in _w.Bus.Read<WeaponFireIntent>())
            {
                var round = Vector3.Normalize(i.TargetPoint - at) * DoorGunMuzzle + v;
                float miss = float.MaxValue;
                for (float t = 0f; t < 2f; t += 0.0005f) miss = MathF.Min(miss, Vector3.Distance(at + round * t, target));
                shots.Add((i.Shooter, miss));
            }
            FlyStandIn(heli, 70f, plane: false);
            return status;
        }

        /// <summary>
        /// ⭐ The doors-off UH-60 ordered to Attack a technical from 1.5 km: it picks <b>DoorGunOrbit</b> (it has no gun of its own), flies
        /// onto a 400 m circle round the target at 100 m and goes round COUNTER-CLOCKWISE, the target on its left door — never hovering
        /// (W4) — and only the LEFT M134 fires, every round (from the gun's aim at its speed plus the helicopter's 70 m/s, A4) passing within
        /// 2 m of the target; when the target is destroyed the attack ends, Success.
        /// ✅ Red-proofs (mutants): the turn's sign flipped ⇒ the target on the right door, whose gun is the one that fires ⇒ red; the lead
        /// without the helicopter's velocity ⇒ the rounds miss by ~30 m ⇒ red; the orbit steered AT the target ⇒ it arrives and hovers ⇒ red.
        /// </summary>
        [Fact]
        public void S6d_TheDoorsOffHelicopter_OrbitsTheTarget_ItsLeftDoorGunFires_UntilTheTargetIsDown()
        {
            var heli = SpawnDoorHeli(new Vector3(-1500f, 0f, 100f), out var leftGun, out var rightGun, out _, out _);
            var tech = SpawnTechnical(Vector3.Zero);
            Assert.Equal(AirAttackProfile.DoorGunOrbit, AirAttackProfiles.Pick(_w, heli, out _, out _, entity: true, Hrot.AI.Behaviors.Orders.StrikeTargetKind.Unspecified, 0));
            var p = new AirAttackParams { Target = tech, Point = Vector3.Zero };
            var ws = default(AirAttackState);
            var executor = new FireAtPointExecutor();
            var shots = new System.Collections.Generic.List<(Entity Gun, float Miss)>();
            uint entered = 0;
            var status = NodeStatus.Running;
            bool onCircle = false;
            float nearest = float.MaxValue, farthest = 0f, turned = 0f, lowest = float.MaxValue, slowest = float.MaxValue;
            float last = MathF.Atan2(0f, -1500f);
            int frame = 0;
            for (; frame < 60 * 120 && status == NodeStatus.Running; frame++)
            {
                if (frame == 60 * 80) _w.GetComponentRW<Health>(tech).Current = 0f;   // the target is destroyed after ~2 turns
                status = OrbitStep(ref p, ref ws, heli, executor, ref entered, frame, Vector3.Zero, shots, leftGun, rightGun);
                var at = _w.GetComponentRO<SimTransform>(heli).Position;
                float r = new Vector2(at.X, at.Y).Length(), bearing = MathF.Atan2(at.Y, at.X);
                if (!onCircle && r < 1.1f * AirAttackNodes.DefaultOrbitRadiusM) onCircle = true;
                if (!onCircle) { last = bearing; continue; }
                nearest = MathF.Min(nearest, r);
                farthest = MathF.Max(farthest, r);
                lowest = MathF.Min(lowest, at.Z);
                slowest = MathF.Min(slowest, _w.GetComponentRO<SimVelocity>(heli).Linear.Length());
                turned += MathF.IEEERemainder(bearing - last, 2f * MathF.PI);
                last = bearing;
            }

            Assert.True(onCircle, "it reached the orbit");
            Assert.InRange(nearest, 0.75f * AirAttackNodes.DefaultOrbitRadiusM, AirAttackNodes.DefaultOrbitRadiusM);
            Assert.InRange(farthest, AirAttackNodes.DefaultOrbitRadiusM, 1.25f * AirAttackNodes.DefaultOrbitRadiusM);
            Assert.True(turned > 2f * MathF.PI, $"counter-clockwise, more than a full turn: {turned:F2} rad");
            Assert.True(slowest > 60f, $"never hovers (W4): slowest {slowest:F1} m/s on the circle");
            Assert.InRange(lowest, AirAttackNodes.DefaultOrbitHeightAgl - 10f, AirAttackNodes.DefaultOrbitHeightAgl + 10f);
            Assert.True(shots.Count >= 60, $"the door gun fired: {shots.Count} rounds");
            Assert.All(shots, s => Assert.Equal(leftGun, s.Gun));
            Assert.All(shots, s => Assert.True(s.Miss < 2f, $"a round passed {s.Miss:F1} m from the target"));
            Assert.Equal(NodeStatus.Success, status);
            Assert.InRange(frame, 60 * 80, 60 * 80 + 2);
        }

        /// <summary>
        /// ⭐ Vignette ⑤'s rule — <i>"a door gunner shot ⇒ his gun falls silent"</i>: the left gunner is hit mid-orbit ⇒ the left M134 never
        /// fires again, the orbit REVERSES (clockwise) and the right door's gun takes over; the right gunner hit too ⇒ every door gun is
        /// silent ⇒ the attack fails (logged), and no round leaves after it.
        /// ✅ Red-proof (mutant): no reversal ⇒ the right gun never bears ⇒ red.
        /// </summary>
        [Fact]
        public void S6d_ADoorGunnerShot_HisGunFallsSilent_TheOrbitTurnsTheOtherDoorIn()
        {
            var heli = SpawnDoorHeli(new Vector3(-1500f, 0f, 100f), out var leftGun, out var rightGun, out var leftGunner, out var rightGunner);
            var tech = SpawnTechnical(Vector3.Zero);
            var p = new AirAttackParams { Target = tech, Point = Vector3.Zero };
            var ws = default(AirAttackState);
            var executor = new FireAtPointExecutor();
            var shots = new System.Collections.Generic.List<(Entity Gun, float Miss)>();
            uint entered = 0;
            var status = NodeStatus.Running;
            int leftDown = 60 * 40, rightDown = 60 * 90, leftShotsBefore = 0, rightShotsBetween = 0, after = 0;
            float turnedBefore = 0f, turnedAfter = 0f, last = MathF.Atan2(0f, -1500f);
            int frame = 0;
            for (; frame < 60 * 120 && status == NodeStatus.Running; frame++)
            {
                if (frame == leftDown) _w.GetComponentRW<Health>(leftGunner).Current = 0f;
                if (frame == rightDown) _w.GetComponentRW<Health>(rightGunner).Current = 0f;
                int before = shots.Count;
                status = OrbitStep(ref p, ref ws, heli, executor, ref entered, frame, Vector3.Zero, shots, leftGun, rightGun);
                for (int i = before; i < shots.Count; i++)
                {
                    if (frame < leftDown && shots[i].Gun == leftGun) leftShotsBefore++;
                    if (frame >= leftDown && shots[i].Gun == leftGun) Assert.Fail($"the left gun fired at frame {frame}, its gunner down at {leftDown}");
                    if (frame >= leftDown && frame < rightDown && shots[i].Gun == rightGun) rightShotsBetween++;
                    if (frame >= rightDown) after++;
                }
                var at = _w.GetComponentRO<SimTransform>(heli).Position;
                float bearing = MathF.Atan2(at.Y, at.X), d = MathF.IEEERemainder(bearing - last, 2f * MathF.PI);
                last = bearing;
                if (frame > leftDown - 60 * 10 && frame < leftDown) turnedBefore += d;
                if (frame > leftDown + 60 * 20 && frame < rightDown) turnedAfter += d;
            }

            Assert.True(leftShotsBefore > 0, "the left gun fired while its gunner stood");
            Assert.True(turnedBefore > 0f, $"counter-clockwise before: {turnedBefore:F2}");
            Assert.True(turnedAfter < -1f, $"clockwise after the left gunner fell: {turnedAfter:F2}");
            Assert.True(rightShotsBetween > 0, "the right door's gun took over");
            Assert.Equal(0, after);
            Assert.Equal(NodeStatus.Failure, status);
            Assert.InRange(frame, rightDown, rightDown + 2);
        }

        /// <summary>
        /// ⭐ A STRIKE (a point, no entity) by the door guns ends after ONE full turn ON THE CIRCLE with rounds out — Success.
        /// ✅ Red-proofs (mutants): the end at two turns ⇒ red; the approach's own turning counted as orbit ⇒ it ends short of a turn
        /// on the circle ⇒ red.
        /// </summary>
        [Fact]
        public void S6d_ADoorGunOrbitOnAPoint_EndsAfterOneFullTurn()
        {
            var heli = SpawnDoorHeli(new Vector3(-1500f, 0f, 100f), out var leftGun, out var rightGun, out _, out _);
            var p = new AirAttackParams { Point = Vector3.Zero };
            var ws = default(AirAttackState);
            var executor = new FireAtPointExecutor();
            var shots = new System.Collections.Generic.List<(Entity Gun, float Miss)>();
            uint entered = 0;
            var status = NodeStatus.Running;
            int frame = 0;
            bool onCircle = false;
            float turned = 0f, last = 0f;
            for (; frame < 60 * 120 && status == NodeStatus.Running; frame++)
            {
                status = OrbitStep(ref p, ref ws, heli, executor, ref entered, frame, Vector3.Zero, shots, leftGun, rightGun);
                var at = _w.GetComponentRO<SimTransform>(heli).Position;
                float bearing = MathF.Atan2(at.Y, at.X);
                if (onCircle) turned += MathF.Abs(MathF.IEEERemainder(bearing - last, 2f * MathF.PI));
                else onCircle = new Vector2(at.X, at.Y).Length() < 1.1f * AirAttackNodes.DefaultOrbitRadiusM;
                last = bearing;
            }
            Assert.Equal(NodeStatus.Success, status);
            Assert.True(turned > 2f * MathF.PI - 0.15f, $"a full turn ON the circle before it ends: {turned:F2} rad");
            Assert.True(shots.Count > 0);
            Assert.InRange(ws.Swept, 2f * MathF.PI, 2f * MathF.PI + 0.1f);
        }

        // ── CE-2191 (§7e) — the aircraft's answer to a missile warning ──────────────────────────────────────────────────────────────

        private const long CmHeli = 9414, TestFlare = 9415, TestChaff = 9416;

        /// <summary>The attack helicopter of <see cref="SpawnHeli"/> with a flare / chaff dispenser as its LAST mount (2), as
        /// <c>BdcTkbCatalog.Dispenser</c> gives the AH-64: 60 flares, 60 chaff, 120 rpm.</summary>
        private Entity SpawnCmHeli(Vector3 at, out Entity pod, out Entity dispenser)
        {
            Reg<AmmoLoadout>();
            var db = (Fdp.Toolkit.Tkb.TkbDatabase)_w.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
            if (!db.TryGetByType(CmHeli, out _))
            {
                var h = new Fdp.Interfaces.TkbTemplate("countermeasure heli", CmHeli);
                h.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
                {
                    new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.MachineGun, TurretIndex = 0,
                        Range = 1000f, InitialAmmunition = 4000, DamagePerHit = 30 },
                    new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Rocket, FixedConeDeg = 8f,
                        Range = 4000f, InitialAmmunition = 38, DamagePerHit = 400 },
                    new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Countermeasure, RateOfFire = 120f,
                        InitialAmmunition = 120, AmmoGuid = TestFlare,
                        Loadout = { new Fdp.Toolkit.Tkb.Domain.AmmoLoadDto { AmmoGuid = TestFlare, Rounds = 60 },
                                    new Fdp.Toolkit.Tkb.Domain.AmmoLoadDto { AmmoGuid = TestChaff, Rounds = 60 } } },
                } });
                db.Register(h);
                db.Register(new Fdp.Interfaces.TkbTemplate("test flare", TestFlare) { DisType = new DISEntityType { Kind = 8, Domain = 2, Category = 2 } });
                db.Register(new Fdp.Interfaces.TkbTemplate("test chaff", TestChaff) { DisType = new DISEntityType { Kind = 8, Domain = 2, Category = 1 } });
            }
            var e = SpawnPlane(at, CmHeli, 4000, Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft);
            pod = _w.CreateEntity();
            _w.AddComponent(pod, new WeaponMountInfo { MountIndex = 1 });
            _w.AddComponent(pod, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = e, InstanceId = 1 });
            _w.AddComponent(pod, new WeaponState { Ammo = 38 });
            dispenser = _w.CreateEntity();
            _w.AddComponent(dispenser, new WeaponMountInfo { MountIndex = 2 });
            _w.AddComponent(dispenser, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = e, InstanceId = 2 });
            _w.AddComponent(dispenser, new WeaponState { Ammo = 120, MaxAmmo = 120 });
            _w.AddComponent(dispenser, AmmoLoadout.From(new[]
            {
                new Fdp.Toolkit.Tkb.Domain.AmmoLoadDto { AmmoGuid = TestFlare, Rounds = 60 },
                new Fdp.Toolkit.Tkb.Domain.AmmoLoadDto { AmmoGuid = TestChaff, Rounds = 60 },
            }));
            return e;
        }

        /// <summary>The warning the SimHost's <c>MissileWarningSensingSystem</c> writes on the aircraft's brain: a missile launched from
        /// <paramref name="from"/> (the bearing to it from the aircraft, 0 = north, clockwise).</summary>
        private void Warn(Entity aircraft, Vector3 from, Fdp.Toolkit.Tkb.Domain.GuidanceKind seeker, float timeToImpact, double now)
        {
            Reg<MissileWarning>();
            var at = _w.GetComponentRO<SimTransform>(aircraft).Position;
            float bearing = MathF.Atan2(from.X - at.X, from.Y - at.Y) * (180f / MathF.PI);
            var w = new MissileWarning { At = now, From = from, BearingDeg = bearing < 0f ? bearing + 360f : bearing, Seeker = seeker, TimeToImpactS = timeToImpact };
            if (_w.HasComponent<MissileWarning>(aircraft)) _w.GetComponentRW<MissileWarning>(aircraft) = w;
            else _w.AddComponent(aircraft, w);
        }

        /// <summary>One frame with the clock: the node, the real weapon executor, the cooldowns drained (the dispenser's too); every
        /// intent of the frame as (mount, ammo, frame).</summary>
        private NodeStatus TimedStep(ref AirAttackParams p, ref AirAttackState ws, Entity aircraft, FireAtPointExecutor executor, ref uint entered,
            int frame, System.Collections.Generic.List<(int Mount, long Ammo, int Frame)> intents, float speed, params Entity[] mounts)
        {
            _w.SetSingleton(new GlobalTime { TotalTime = frame * Dt });
            var status = AirAttackNodes.Run(ref p, ref ws, aircraft, _w);
            ref var wc = ref _w.GetComponentRW<WeaponChannel>(aircraft);
            if (wc.ActiveAction == CombatConstants.ActionIdFireAtPoint)
            {
                if (wc.ActionInstanceId != entered) { executor.OnEnter(aircraft, ref wc, _w); entered = wc.ActionInstanceId; }
                executor.Execute(aircraft, ref wc, _w, Dt);
                AnswerFired();   // ⭐ Q94 A-3 — the muscle answers every ask "fired" (no FireProcessingSystem in this harness)
            }
            foreach (var m in mounts)
            {
                ref var w = ref _w.GetComponentRW<WeaponState>(m);
                w.CooldownSecondsRemaining = MathF.Max(0f, w.CooldownSecondsRemaining - Dt);
            }
            _w.Bus.SwapBuffers();
            foreach (ref readonly var i in _w.Bus.Read<WeaponFireIntent>()) intents.Add((i.WeaponIndex, i.AmmoGuid, frame));
            FlyStandIn(aircraft, speed, plane: false);
            return status;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2191</c> (§7e) — a helicopter on a running-fire pass is warned of an infra-red missile launched off its left bow: its
        /// weapons stop at once (no round while it evades), it drops <see cref="AirAttackNodes.DecoySalvo"/> FLARES from its dispenser (one
        /// per 0.5 s interval, the dispenser's 120 rpm — never chaff, the seeker is infra-red), it turns to put the missile on its BEAM and a
        /// little away (its track within 35° of square to the line to the missile, never closing on it) and dives to
        /// <see cref="AirAttackNodes.EvadeHeightAglRotor"/>; when the missile is past it comes round — a fresh run-in, the aborted pass
        /// counted — fires its rockets and gun on the new pass and succeeds.
        /// ✅ Red-proofs (mutants): no evasion stage ⇒ the rockets ripple through the warning ⇒ red; chaff for an infra-red seeker ⇒ red;
        /// the break flown AT the missile ⇒ the track closes on it ⇒ red; no come-round ⇒ the attack ends without a rocket ⇒ red.
        /// </summary>
        [Fact]
        public void S7e_AWarnedHelicopter_StopsFiring_DropsFlares_BreaksOntoTheMissilesBeam_ThenComesRound()
        {
            var heli = SpawnCmHeli(new Vector3(-4000f, 300f, 60f), out var pod, out var dispenser);
            var p = new AirAttackParams { Point = Vector3.Zero };
            var ws = default(AirAttackState);
            var executor = new FireAtPointExecutor();
            var intents = new System.Collections.Generic.List<(int Mount, long Ammo, int Frame)>();
            uint entered = 0;
            var status = NodeStatus.Running;
            int warnedAt = -1;
            var launcher = new Vector3(-1500f, 1800f, 0f);   // off the left bow of a run-in flown east
            float maxCosToMissile = -1f, lowest = float.MaxValue;
            int frame = 0;
            for (; frame < 60 * 300 && status == NodeStatus.Running; frame++)
            {
                var at = _w.GetComponentRO<SimTransform>(heli).Position;
                if (warnedAt < 0 && new Vector2(at.X, at.Y).Length() < 3200f)
                {
                    warnedAt = frame;
                    Warn(heli, launcher, Fdp.Toolkit.Tkb.Domain.GuidanceKind.InfraRed, 5f, frame * Dt);
                }
                status = TimedStep(ref p, ref ws, heli, executor, ref entered, frame, intents, 60f, heli, pod, dispenser);
                if (warnedAt >= 0 && frame > warnedAt + 60 * 2 && frame < warnedAt + 60 * 7)
                {
                    var v = _w.GetComponentRO<SimVelocity>(heli).Linear;
                    var flat = new Vector2(v.X, v.Y);
                    var now = _w.GetComponentRO<SimTransform>(heli).Position;
                    var toMissile = Vector2.Normalize(new Vector2(launcher.X - now.X, launcher.Y - now.Y));
                    if (flat.LengthSquared() > 1f) maxCosToMissile = MathF.Max(maxCosToMissile, Vector2.Dot(Vector2.Normalize(flat), toMissile));
                    lowest = MathF.Min(lowest, now.Z);
                }
            }

            Assert.True(warnedAt > 0, "the warning was raised on the run-in");
            int evadeEnd = warnedAt + (int)((5f + AirAttackNodes.EvadeMarginS) / Dt);
            var during = intents.FindAll(i => i.Frame >= warnedAt && i.Frame < evadeEnd);
            Assert.DoesNotContain(during, i => i.Mount != 2);   // no round while it evades
            var flares = intents.FindAll(i => i.Mount == 2);
            Assert.Equal(AirAttackNodes.DecoySalvo, flares.Count);
            Assert.All(flares, f => Assert.Equal(TestFlare, f.Ammo));
            Assert.All(flares, f => Assert.InRange(f.Frame, warnedAt, warnedAt + 60 * 3));
            Assert.Equal(60 - AirAttackNodes.DecoySalvo, Countermeasures.Left(_w, heli, DecoyKind.Flare));
            Assert.Equal(60, Countermeasures.Left(_w, heli, DecoyKind.Chaff));
            Assert.InRange(maxCosToMissile, -1f, MathF.Sin(35f * MathF.PI / 180f));   // on the beam, never closing on the missile
            Assert.InRange(lowest, AirAttackNodes.EvadeHeightAglRotor - 5f, AirAttackNodes.EvadeHeightAglRotor + 10f);
            var rocketsAfter = intents.FindAll(i => i.Mount == 1 && i.Frame >= evadeEnd);
            Assert.Equal(AirAttackNodes.DefaultRipple, rocketsAfter.Count);   // the come-round pass fires
            Assert.True(ws.Passes >= 1, "the aborted pass is counted");
            Assert.Equal(NodeStatus.Success, status);
        }

        /// <summary>
        /// ⭐ <c>CE-2191</c> — the decoy follows the seeker: a radar missile gets CHAFF; a command-guided one (a wire / beam rider no decoy
        /// fools) gets none — the break alone; and a warning older than its missile's flight (an attack that starts after it) is ignored.
        /// ✅ Red-proof (mutant): flares for every seeker ⇒ red.
        /// </summary>
        [Fact]
        public void S7e_TheDecoyFollowsTheSeeker_ChaffForRadar_NoneForCommand_AStaleWarningIsIgnored()
        {
            Assert.Equal(DecoyKind.Flare, AirAttackNodes.DecoyFor(Fdp.Toolkit.Tkb.Domain.GuidanceKind.InfraRed));
            Assert.Equal(DecoyKind.Chaff, AirAttackNodes.DecoyFor(Fdp.Toolkit.Tkb.Domain.GuidanceKind.Radar));
            Assert.Null(AirAttackNodes.DecoyFor(Fdp.Toolkit.Tkb.Domain.GuidanceKind.Command));
            Assert.Null(AirAttackNodes.DecoyFor(Fdp.Toolkit.Tkb.Domain.GuidanceKind.FireAndForget));

            foreach (var (seeker, ammo, decoys) in new[] { (Fdp.Toolkit.Tkb.Domain.GuidanceKind.Radar, TestChaff, AirAttackNodes.DecoySalvo),
                                                           (Fdp.Toolkit.Tkb.Domain.GuidanceKind.Command, 0L, 0) })
            {
                var heli = SpawnCmHeli(new Vector3(-4000f, 300f, 60f), out var pod, out var dispenser);
                var p = new AirAttackParams { Point = Vector3.Zero };
                var ws = default(AirAttackState);
                var executor = new FireAtPointExecutor();
                var intents = new System.Collections.Generic.List<(int Mount, long Ammo, int Frame)>();
                uint entered = 0;
                for (int frame = 0; frame < 60 * 8; frame++)
                {
                    if (frame == 60) Warn(heli, new Vector3(-3000f, 2000f, 0f), seeker, 5f, frame * Dt);
                    TimedStep(ref p, ref ws, heli, executor, ref entered, frame, intents, 60f, heli, pod, dispenser);
                }
                var released = intents.FindAll(i => i.Mount == 2);
                Assert.Equal(decoys, released.Count);
                Assert.All(released, r => Assert.Equal(ammo, r.Ammo));
            }

            // a warning raised 30 s before the attack starts (its 5 s missile long gone) changes nothing
            var late = SpawnCmHeli(new Vector3(-4000f, 300f, 60f), out var pod2, out var dispenser2);
            Warn(late, new Vector3(-3000f, 2000f, 0f), Fdp.Toolkit.Tkb.Domain.GuidanceKind.InfraRed, 5f, 1.0);
            var p2 = new AirAttackParams { Point = Vector3.Zero };
            var ws2 = default(AirAttackState);
            var ex2 = new FireAtPointExecutor();
            var in2 = new System.Collections.Generic.List<(int Mount, long Ammo, int Frame)>();
            uint en2 = 0;
            for (int frame = 60 * 31; frame < 60 * 34; frame++)
                TimedStep(ref p2, ref ws2, late, ex2, ref en2, frame, in2, 60f, late, pod2, dispenser2);
            Assert.DoesNotContain(in2, i => i.Mount == 2);
        }

        /// <summary>
        /// ⭐ <c>CE-2191</c> — a door-gun orbit warned mid-circle breaks (its door gun stops) and, the missile past, goes back to the SAME
        /// orbit — its turn kept, no new pass counted — and its door gun fires again.
        /// ✅ Red-proof (mutant): resuming to a run-in instead of the orbit ⇒ the orbit's turn is lost ⇒ red.
        /// </summary>
        [Fact]
        public void S7e_ADoorGunOrbitWarnedMidCircle_Breaks_ThenResumesTheSameOrbit()
        {
            var heli = SpawnDoorHeli(new Vector3(-1500f, 0f, 100f), out var leftGun, out var rightGun, out _, out _);
            var tech = SpawnTechnical(Vector3.Zero);
            var p = new AirAttackParams { Target = tech, Point = Vector3.Zero };
            var ws = default(AirAttackState);
            var executor = new FireAtPointExecutor();
            var shots = new System.Collections.Generic.List<(Entity Gun, float Miss)>();
            uint entered = 0;
            int warnedAt = -1, firedBefore = 0;
            float sweptAtWarn = 0f;
            for (int frame = 0; frame < 60 * 70; frame++)
            {
                if (warnedAt < 0 && ws.Swept > MathF.PI / 2f)
                {
                    warnedAt = frame;
                    sweptAtWarn = ws.Swept;
                    firedBefore = shots.Count;
                    Warn(heli, new Vector3(1500f, 1500f, 0f), Fdp.Toolkit.Tkb.Domain.GuidanceKind.InfraRed, 4f, frame * Dt);
                }
                OrbitStep(ref p, ref ws, heli, executor, ref entered, frame, Vector3.Zero, shots, leftGun, rightGun);
                if (warnedAt >= 0 && frame > warnedAt + 2 && frame < warnedAt + 60 * 6)
                    Assert.Equal(firedBefore, shots.Count);   // the door gun is silent while it breaks
            }
            Assert.True(warnedAt > 0, "warned on the circle");
            Assert.True(firedBefore > 0, "the gun fired before the warning");
            Assert.Equal(0, ws.Passes);
            Assert.True(ws.Swept > sweptAtWarn + MathF.PI / 2f, $"the same orbit carried on: swept {sweptAtWarn:F2} → {ws.Swept:F2} rad");
            Assert.True(shots.Count > firedBefore, "the door gun fires again on the resumed orbit");
        }
    
        // ═══════════════════════════════════════════════════════ ⭐ Slice 6e (§9i) — Dive: the FPV drone

        private const long Drone = 9412;
        private const float DroneSpeed = 30f, DroneClimb = 8f, DroneAccel = 15f;

        /// <summary>An FPV drone as <c>BdcTkbCatalog.DefineFpvDrone</c> states it: a Rotorcraft that DETONATES on impact, 30 m/s, climbs 8 m/s,
        /// its warhead its mount 0 (450 damage); hostile.</summary>
        private Entity SpawnDrone(Vector3 at, float enduranceSeconds = 0f)
        {
            if (_w.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() is Fdp.Toolkit.Tkb.TkbDatabase db && !db.TryGetByType(Drone, out _))
            {
                var t = new Fdp.Interfaces.TkbTemplate("test fpv drone", Drone);
                t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
                    { InitialAmmunition = 1, Range = 0.8f, DamagePerHit = 450 } } });
                db.Register(t);
            }
            var e = SpawnPlane(at, Drone, 1, Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft);
            // ⭐ the FPV drone's own catalogue data (BdcTkbCatalog.DefineFpvDrone: 30 m/s, climb 8, 120°/s, 15 m/s², its warhead), flown by the
            //   REAL RotorcraftModel (FlyDrone) — 📐 a stand-in that changed velocity instantly hit an evading tank to 0.1 m while the live
            //   drone, flying along its nose at 15 m/s², overshot it by 10 m and looped into the ground (CE-2213)
            _w.SetComponent(e, Fdp.Toolkit.Motion.FlightParams.From(new Fdp.Toolkit.Tkb.Domain.FlightParamsDto
            {
                MaxSpeedMps = DroneSpeed, ClimbMps = DroneClimb, AccelMps2 = DroneAccel, DetonatesOnImpact = true, EnduranceSeconds = enduranceSeconds,
            }));
            _w.AddComponent(e, new Fdp.Toolkit.Motion.FlightState { Airborne = 1 });
            _w.AddComponent(e, new NavigationIntent());
            _w.AddComponent(e, new NavigationStatus());
            _w.AddComponent(e, new Health { Current = 10, Max = 10 });
            _w.SetAuthority<SimTransform>(e, true);   // the air system moves only owned bodies
            _w.AddComponent(e, new EntityInfo { ForceId = ForceId.Hostile });
            return e;
        }

        private Entity SpawnGround(Vector3 at, float health, bool vehicle, ForceId force)
        {
            var t = _w.CreateEntity();
            _w.AddComponent(t, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(t, new SimVelocity());
            _w.AddComponent(t, new Health { Current = health, Max = health });
            _w.AddComponent(t, new EntityInfo { ForceId = force });
            if (vehicle) _w.AddComponent(t, new global::CarKinem.Core.VehicleParams { Class = global::CarKinem.Core.VehicleClass.Tank });
            return t;
        }

        private unsafe void Remember(Entity drone, params Entity[] contacts)
        {
            var mem = new Fdp.Toolkit.Perception.Components.TargetMemory();
            foreach (var c in contacts)
            {
                var at = _w.GetComponentRO<SimTransform>(c).Position;
                mem.EntityIds[mem.Count] = (long)c.PackedValue;
                mem.PositionsX[mem.Count] = at.X; mem.PositionsY[mem.Count] = at.Y; mem.PositionsZ[mem.Count] = at.Z;
                mem.Freshness[mem.Count] = 1f;
                mem.Count++;
            }
            if (_w.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(drone)) _w.SetComponent(drone, mem);
            else _w.AddComponent(drone, mem);
        }

        /// <summary>The stand-in for <c>RotorcraftModel</c>'s point move: horizontal toward the point at up to its speed, braking √(2·a·d);
        /// vertical capped at ±ClimbMps — as the model flies it; ⭐ CE-2211 a strike (arrival radius 0) neither brakes nor caps the dive.</summary>
        private readonly Fdp.Toolkit.Motion.AirMotionSystem _air = new();
        private uint _droneIntent;

        /// <summary>The drone's MoveTo, flown by the real air motion system: the order becomes its NavigationIntent (a new id when it
        /// changes, as MoveToExecutor stamps one), the model moves it, and its status answers the channel.</summary>
        private void FlyDrone(Entity e)
        {
            var move = MoveOf(e);
            ref var intent = ref _w.GetComponentRW<NavigationIntent>(e);
            if (intent.Mode != NavigationMode.DirectPoint || intent.FinalDestination != move.Destination
                || intent.TargetSpeed != move.Speed || intent.ArrivalRadius != move.ArrivalRadius)
                intent = new NavigationIntent
                {
                    Mode = NavigationMode.DirectPoint, FinalDestination = move.Destination, TargetSpeed = move.Speed,
                    ArrivalRadius = move.ArrivalRadius, IntentId = ++_droneIntent,
                };
            _air.Execute(_w, Dt);
            var status = _w.GetComponentRO<NavigationStatus>(e);
            _w.GetComponentRW<LocomotionChannel>(e).Status = status.Result == NavigationResult.Arrived && status.IntentId == intent.IntentId
                ? NodeStatus.Success : NodeStatus.Running;
        }

        private NodeStatus DroneStep(ref AirAttackParams p, ref AirAttackState ws, Entity drone, int frame)
        {
            _w.SetSingleton(new GlobalTime { TotalTime = frame * Dt });
            var status = AirAttackNodes.Run(ref p, ref ws, drone, _w);
            FlyDrone(drone);
            return status;
        }

        /// <summary>
        /// ⭐ §9i ① — an FPV drone ordered to Attack a tank 600 m off: it picks <b>Dive</b> (it detonates on impact), pursues at 60 m above
        /// the tank, begins the dive within height / tan 60° (≈ 35 m, horizontal), every dive point is ON the dive line over the tank's middle
        /// (never below it), it comes down on
        /// it from ABOVE (the last 5 m of descent steeper than 45°), and the attack ends — Success — the frame the tank is destroyed.
        /// ✅ Red-proofs (mutants): no Dive pick ⇒ no profile, Failure at once; the dive never begun (pursue only) ⇒ it hovers 60 m over
        /// the tank ⇒ red; the dive aimed at the pursuit height ⇒ it never comes down ⇒ red.
        /// </summary>
        [Fact]
        public void S6e_AnFpvDrone_PursuesATank_AndDivesOnItFromAbove_UntilItIsDestroyed()
        {
            var drone = SpawnDrone(new Vector3(-600f, 0f, 20f));
            var tank = SpawnGround(Vector3.Zero, 1000f, vehicle: true, ForceId.Friend);
            Assert.Equal(AirAttackProfile.Dive, AirAttackProfiles.Pick(_w, drone, out var mount, out _, entity: true, Hrot.AI.Behaviors.Orders.StrikeTargetKind.Unspecified, 0));
            Assert.Equal(0, mount);
            var p = new AirAttackParams { Target = tank, Point = Vector3.Zero };
            var ws = default(AirAttackState);
            var middle = Fdp.Toolkit.Combat.AimPoint.Middle(_w, tank, out _, out _);
            float maxPursuit = 0f, diveStartFlat = -1f;
            var track = new System.Collections.Generic.List<Vector3>();
            NodeStatus status = NodeStatus.Running;
            int frame = 0;
            for (; frame < 60 * 120 && status == NodeStatus.Running; frame++)
            {
                status = DroneStep(ref p, ref ws, drone, frame);
                var at = _w.GetComponentRO<SimTransform>(drone).Position;
                var dest = MoveOf(drone).Destination;
                bool diving = new Vector2(dest.X - middle.X, dest.Y - middle.Y).Length() < 0.01f && dest.Z < 59.9f;   // down the dive line
                if (diving) Assert.True(dest.Z >= middle.Z - 0.01f, $"the dive line is never below the tank's middle: {dest}");
                if (!diving) maxPursuit = MathF.Max(maxPursuit, dest.Z);
                else
                {
                    if (diveStartFlat < 0f) diveStartFlat = new Vector2(at.X, at.Y).Length();
                    track.Add(at);
                }
                if (Vector3.Distance(at, middle) < 1f) break;   // the Muscle's impact would meet the hull here
            }
            Assert.Equal(AirAttackProfile.Dive, ws.Profile);
            Assert.InRange(maxPursuit, 59.9f, 60.1f);                                   // the pursuit at the cruise height above the tank
            Assert.InRange(diveStartFlat, 0f, 36f);                                     // ≤ 60 / tan 60° (+ one frame)
            Assert.True(track.Count > 0 && Vector3.Distance(track[^1], middle) < 1f, $"the drone came down on the tank — last at {(track.Count > 0 ? track[^1] : default)}");
            int k = track.FindIndex(q => q.Z <= middle.Z + 5f);
            var leg = track[^1] - track[Math.Max(0, k - 1)];
            Assert.True(-leg.Z >= new Vector2(leg.X, leg.Y).Length(), $"from above: the last leg {leg}");   // steeper than 45°
            _w.GetComponentRW<Health>(tank).Current = 0f;                               // the warhead's work (DroneImpactSystem)
            Assert.Equal(NodeStatus.Success, DroneStep(ref p, ref ws, drone, ++frame));
        }

        /// <summary>
        /// ⭐ §9i ④ (found live, ua-drone-shelter) — a tank FLEEING at 15 m/s is still hit, from above: the dive LEADS it (its velocity × the
        /// time to reach it) and follows the dive line over it, so the drone comes down on its roof within 40 s.
        /// ✅ Red-proof (mutant): no lead ⇒ red (measured, see §9i). 📐 Live before the fix: it trailed an M1 ~10 m behind at the hull's height
        /// for a minute — the model braked for the point and capped the dive at 8 m/s (CE-2211).
        /// </summary>
        [Fact]
        public void S6e_ADroneDivesOnAFleeingTank_LeadsIt_AndHitsItFromAbove()
        {
            var drone = SpawnDrone(new Vector3(400f, 0f, 60f));
            var tank = SpawnGround(Vector3.Zero, 1000f, vehicle: true, ForceId.Friend);
            var flee = new Vector3(-15f, 0f, 0f);
            _w.GetComponentRW<SimVelocity>(tank).Linear = flee;
            var p = new AirAttackParams { Target = tank, Point = Vector3.Zero };
            var ws = default(AirAttackState);
            var track = new System.Collections.Generic.List<Vector3>();
            bool hit = false;
            int frame = 0;
            for (; frame < 60 * 40 && !hit; frame++)
            {
                DroneStep(ref p, ref ws, drone, frame);
                _w.GetComponentRW<SimTransform>(tank).Position += flee * Dt;
                var at = _w.GetComponentRO<SimTransform>(drone).Position;
                var middle = Fdp.Toolkit.Combat.AimPoint.Middle(_w, tank, out _, out _);
                track.Add(at - middle);
                hit = Vector3.Distance(at, middle) < 1.5f;
            }
            Assert.True(hit, $"the drone reached the fleeing tank within 40 s — last offset {track[^1]}");
            int k = track.FindIndex(q => q.Z <= 5f);
            var leg = track[^1] - track[Math.Max(0, k - 1)];
            Assert.True(-leg.Z >= new Vector2(leg.X, leg.Y).Length(), $"from above (relative to the tank): the last leg {leg}");
        }

        /// <summary>
        /// ⭐ §9i ⑤ (CE-2212's live run) — a tank EVADING the drone (Exposure Cycle §8's legs: 40 m away from it, swerving ±35° on
        /// alternate sides) is still hit: at 7 and 10 m/s, from three points in its leg cycle, the drone comes within 1.5 m of its middle.
        /// 📐 Measured `2026-10-16`: closest 0.02–0.09 m in all six — so the ~5 m miss seen live is NOT the dive's guidance (the rails fly
        /// with no network between the brain and the flight). ⚠ Measured: the no-lead mutant leaves THIS rail green (at these speeds the 0.2 s
        /// re-aim follows the swerve alone; the fleeing rail is the lead's red-proof) — this rail guards the re-aim against a swerving target.
        /// </summary>
        [Fact]
        public void S6e_ADroneDivesOnAnEvadingTank_ZigZagLegs_AndStillHitsIt()
        {
            var sb = new System.Text.StringBuilder();
            bool all = true;
            int caseNo = 0;
            foreach (float speed in new[] { 7f, 10f })
            foreach (float phase in new[] { 0f, 1.5f, 3f })
            {
                var o = new Vector3(0f, 5000f * (++caseNo), 0f);
                var drone = SpawnDrone(o + new Vector3(400f, 0f, 60f));
                var tank = SpawnGround(o, 1000f, vehicle: true, ForceId.Friend);
                var p = new AirAttackParams { Target = tank, Point = o };
                var ws = default(AirAttackState);
                float best = float.MaxValue; double legT = phase; int side = 0;
                for (int frame = 0; frame < 60 * 40; frame++)
                {
                    legT += Dt;
                    if (legT >= 40f / speed) { legT = 0; side ^= 1; }
                    float a = MathF.PI + (side == 0 ? 1f : -1f) * 35f * MathF.PI / 180f;   // away from the drone (+x), swerving
                    var v = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f) * speed;
                    _w.GetComponentRW<SimVelocity>(tank).Linear = v;
                    DroneStep(ref p, ref ws, drone, frame);
                    _w.GetComponentRW<SimTransform>(tank).Position += v * Dt;
                    var at = _w.GetComponentRO<SimTransform>(drone).Position;
                    var middle = Fdp.Toolkit.Combat.AimPoint.Middle(_w, tank, out _, out _);
                    best = MathF.Min(best, Vector3.Distance(at, middle));
                    if (at.Z < 0.5f) break;
                }
                sb.Append($"speed {speed} phase {phase}: closest {best:F2} m; ");
                all &= best < 1.5f;
            }
            Assert.True(all, sb.ToString());
        }

        /// <summary>
        /// ⭐ §9i ② — a drone sent to a POINT searches it and dives on what it remembers there: of a tank (1000 hp — its warhead's 450 is
        /// 0.45 of a kill, a vehicle ×1), a rifleman (100 hp — a whole kill, but a person ×0.3) and its OWN side's tank, it picks the
        /// enemy TANK; a contact outside the search radius is passed over.
        /// ✅ Red-proofs (mutants): the person weight 1 ⇒ the rifleman (1.0 &gt; 0.45) ⇒ red; no force test ⇒ its own tank (nearer the point,
        /// the same score) can win ⇒ red.
        /// </summary>
        [Fact]
        public void S6e_ADroneOnAPoint_SearchesIt_AndPicksTheEnemyTank_OverARiflemanAndItsOwnSide()
        {
            var drone = SpawnDrone(new Vector3(-600f, 0f, 60f));
            var own = SpawnGround(new Vector3(5f, 0f, 0f), 1000f, vehicle: true, ForceId.Hostile);
            var rifleman = SpawnGround(new Vector3(-30f, 10f, 0f), 100f, vehicle: false, ForceId.Friend);
            var tank = SpawnGround(new Vector3(80f, 40f, 0f), 1000f, vehicle: true, ForceId.Friend);
            var far = SpawnGround(new Vector3(400f, 0f, 0f), 1000f, vehicle: true, ForceId.Friend);
            Remember(drone, own, far, rifleman, tank);
            Assert.Equal(tank, DroneTargets.Pick(_w, drone, Vector3.Zero, AirAttackNodes.DefaultSearchRadiusM));
            var p = new AirAttackParams { Point = Vector3.Zero };
            var ws = default(AirAttackState);
            for (int frame = 0; frame < 5; frame++) DroneStep(ref p, ref ws, drone, frame);
            Assert.Equal(tank, p.Target);                                               // the search handed its pick to the pursuit
            Assert.Equal(Vector3.Zero + new Vector3(80f, 40f, 0f), p.Point);
        }

        /// <summary>
        /// ⭐ §9i ② (the search) — with nothing remembered in the area the drone CIRCLES the point at the cruise height (150 m round it), it
        /// does not hover on it; ⭐ ③ the battery — at <c>EnduranceSeconds</c> it dives straight down where it is.
        /// ✅ Red-proofs (mutants): the circle steered at the point ⇒ it hovers over it ⇒ red; no battery ⇒ it circles on ⇒ red.
        /// </summary>
        [Fact]
        public void S6e_ADroneFindingNothing_CirclesThePoint_ThenItsBatteryEndsInADiveStraightDown()
        {
            var drone = SpawnDrone(new Vector3(-600f, 0f, 60f), enduranceSeconds: 60f);
            var p = new AirAttackParams { Point = Vector3.Zero };
            var ws = default(AirAttackState);
            float minR = float.MaxValue, maxR = 0f, swept = 0f, last = float.NaN;
            int frame = 0;
            for (; frame < 60 * 59; frame++)
            {
                DroneStep(ref p, ref ws, drone, frame);
                var at = _w.GetComponentRO<SimTransform>(drone).Position;
                float r = new Vector2(at.X, at.Y).Length();
                if (frame > 60 * 30)   // well after the 600 m approach (20 s at 30 m/s)
                {
                    minR = MathF.Min(minR, r); maxR = MathF.Max(maxR, r);
                    float b = MathF.Atan2(at.Y, at.X);
                    if (!float.IsNaN(last)) { float d = b - last; if (d > MathF.PI) d -= 2f * MathF.PI; if (d < -MathF.PI) d += 2f * MathF.PI; swept += d; }
                    last = b;
                }
            }
            Assert.InRange(minR, 100f, 200f);
            Assert.InRange(maxR, 100f, 200f);
            Assert.True(MathF.Abs(swept) > MathF.PI / 2f, $"it went round: {swept:F2} rad");
            for (; frame < 60 * 62; frame++) DroneStep(ref p, ref ws, drone, frame);
            var here = _w.GetComponentRO<SimTransform>(drone).Position;
            var dest = MoveOf(drone).Destination;
            Assert.Equal(1, ws.BatteryOut);
            Assert.True(new Vector2(dest.X - here.X, dest.Y - here.Y).Length() < 30f * 0.25f && dest.Z <= 0.01f,
                $"straight down: dest {dest}, at {here}");
        }
    }
}
