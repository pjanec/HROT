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
    }
}
