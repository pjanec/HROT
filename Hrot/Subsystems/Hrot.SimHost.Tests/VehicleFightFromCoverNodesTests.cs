using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using CarKinemVehicleClass = global::CarKinem.Core.VehicleClass;
using CarKinemVehicleParams = global::CarKinem.Core.VehicleParams;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Xunit;
using CoverClaim = Fdp.Toolkit.Combat.Components.CoverClaim;
using Health = Fdp.Toolkit.Combat.Components.Health;
using TkbIdentity = Fdp.Toolkit.Replication.Components.TkbIdentity;
using WeaponState = Fdp.Toolkit.Combat.Components.WeaponState;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2165</c> V4 — <see cref="VehicleFightFromCoverNodes.VehicleFightFromCover"/> (📄
    /// docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md §4): hide, keep the gun on the threat, drive out to a firing point,
    /// fire, withdraw — REVERSE into the same hide (a tank) or SCOOT forward to the next (a light vehicle). The node runs on a world whose
    /// body, weapon and EQS are FAKED here, as <see cref="PeekAndFireNodesTests"/> does (a move arrives on the next step, a weapon fires
    /// one round a step, the test writes the sensors' answers) — the node's decisions are under test, not the executors.
    /// </summary>
    public sealed unsafe class VehicleFightFromCoverNodesTests
    {
        private static readonly Vector3 EnemyAt = new(80f, 0f, 0f);
        private static readonly Vector3 HideA = new(-10f, 0f, 0f), FiringA = new(-2f, 4f, 0f), HideB = new(-10f, 30f, 0f), FiringB = new(-2f, 26f, 0f);
        private const long TankType = 9201, TechnicalType = 9202, RpgManType = 9203, RiflemanType = 9204;

        private sealed class Duel
        {
            public readonly EntityRepository Repo = new();
            public readonly Entity Self, Enemy;
            public VehicleFightFromCoverParams P;
            public VehicleFightFromCoverState Ws;
            public double Time;
            public readonly List<(Vector3 To, bool Reverse)> Moves = new();
            public readonly List<Entity> AimedAt = new();
            public int AimedRounds, TrackedSteps;
            /// <summary>The fake weapon reports a guided round in flight (the executor's <see cref="ActionReason.Guiding"/>).</summary>
            public bool Guiding;
            private uint _moveInst, _fireInst;
            private int _fired;
            private long _frame;

            public Duel(VehicleFightFromCoverParams p, bool seen = true)
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                Fdp.Toolkit.Utility.StandardInputs.RegisterAll();
                if (!Repo.IsComponentTypeRegistered<ActiveSensorTracks>()) Repo.RegisterComponent<ActiveSensorTracks>();
                if (!Repo.IsComponentTypeRegistered<ActionStatus>()) Repo.RegisterComponent<ActionStatus>();
                if (!Repo.IsComponentTypeRegistered<CarKinemVehicleParams>()) Repo.RegisterComponent<CarKinemVehicleParams>();
                if (!Repo.IsComponentTypeRegistered<TkbIdentity>()) Repo.RegisterComponent<TkbIdentity>();
                P = p;

                Self = Repo.CreateEntity();
                Repo.AddComponent(Self, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });   // facing +X
                Repo.AddComponent(Self, new BehaviorState());
                Repo.AddComponent(Self, new LocomotionChannel());
                Repo.AddComponent(Self, new WeaponChannel());
                Repo.AddComponent(Self, new ActionStatus());
                Repo.AddComponent(Self, new WeaponState { Ammo = 40, MaxAmmo = 40 });
                Repo.AddComponent(Self, new RecentSenses());
                Repo.AddComponent(Self, new ActiveSensorTracks());
                Repo.AddComponent(Self, new CarKinemVehicleParams { Class = CarKinemVehicleClass.Tank });

                Enemy = Repo.CreateEntity();
                Repo.AddComponent(Enemy, new SimTransform { Position = EnemyAt, Rotation = Quaternion.Identity });
                Repo.AddComponent(Self, new TargetMemory());
                ref var mem = ref Repo.GetComponentRW<TargetMemory>(Self);
                mem.EntityIds[0] = (long)Enemy.PackedValue;
                mem.Freshness[0] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
                mem.Modalities[0] = (byte)SensorModality.Visual;
                mem.PositionsX[0] = EnemyAt.X; mem.PositionsY[0] = EnemyAt.Y; mem.PositionsZ[0] = EnemyAt.Z;
                mem.Count = 1;
                if (seen) See(true);
                Clock();
            }

            public void See(bool seen)
            {
                ref var t = ref Repo.GetComponentRW<ActiveSensorTracks>(Self);
                t.Count = 0;
                if (!seen) return;
                t.EntityIds[0] = (long)Enemy.PackedValue;
                t.Modalities[0] = (byte)SensorModality.Visual;
                t.Count = 1;
            }

            private void Clock()
            {
                Repo.SetSingleton(new GlobalTime { TotalTime = Time, DeltaTime = 0.1f, TimeScale = 1f, FrameNumber = ++_frame });
                Repo.SetSimulationTime((float)Time);
            }

            public NodeStatus Step(double dt = 0.1)
            {
                Clock();
                var status = VehicleFightFromCoverNodes.VehicleFightFromCover(ref P, ref Ws, Self, Repo);
                Repo.FlushCommandBuffers();
                Body();
                Weapon();
                Time += dt;
                return status;
            }

            public void Run(double seconds) { double end = Time + seconds; while (Time < end) Step(); }

            public VehiclePhase RunUntil(VehiclePhase phase, double maxSeconds = 30)
            {
                double end = Time + maxSeconds;
                while (Time < end) { Step(); if (Ws.Phase == phase) return phase; }
                return Ws.Phase;
            }

            private void Body()
            {
                ref var loco = ref Repo.GetComponentRW<LocomotionChannel>(Self);
                if (loco.ActiveAction != NavigationConstants.ActionIdMoveTo || loco.Status != NodeStatus.Running) return;
                if (loco.ActionInstanceId != _moveInst)
                {
                    _moveInst = loco.ActionInstanceId;
                    fixed (byte* src = loco.Params) Moves.Add((((MoveToParams*)src)->Destination, ((MoveToParams*)src)->ReverseAllowed == 1));
                    return;   // arrives on the next step
                }
                loco.Status = NodeStatus.Success;
            }

            private void Weapon()
            {
                ref var ch = ref Repo.GetComponentRW<WeaponChannel>(Self);
                if (ch.ActiveAction == CombatConstants.ActionIdTrackTarget && ch.Status == NodeStatus.Running) { TrackedSteps++; return; }
                if (ch.ActiveAction != CombatConstants.ActionIdAimAndFire || ch.Status != NodeStatus.Running) return;
                int rounds = Unsafe.As<byte, AimAndFireParams>(ref ch.Params[0]).Rounds;
                if (ch.ActionInstanceId != _fireInst)
                {
                    _fireInst = ch.ActionInstanceId;
                    _fired = 0;
                    AimedAt.Add(Unsafe.As<byte, AimAndFireParams>(ref ch.Params[0]).Target);
                }
                Repo.GetComponentRW<ActionStatus>(Self).Weapon.Reason = Guiding ? ActionReason.Guiding : ActionReason.Firing;
                if (Guiding) return;   // a round in flight: the next is not granted
                _fired++;
                AimedRounds++;
                if (rounds > 0 && _fired >= rounds) ch.Status = NodeStatus.Success;
            }

            public bool Firing
            {
                get
                {
                    ref readonly var ch = ref Repo.GetComponentRO<WeaponChannel>(Self);
                    return ch.ActiveAction == CombatConstants.ActionIdAimAndFire && ch.Status == NodeStatus.Running;
                }
            }

            public bool Tracking
            {
                get
                {
                    ref readonly var ch = ref Repo.GetComponentRO<WeaponChannel>(Self);
                    return ch.ActiveAction == CombatConstants.ActionIdTrackTarget;
                }
            }

            public Entity Sensor(int site) => EqsChildSensor.Find(Repo, Self, site);

            /// <summary>Writes a sensor's answer: points in rank order (score falls by 0.1 per rank).</summary>
            public void Answer(int site, uint tick, params Vector3[] points)
            {
                var sensor = Sensor(site);
                Assert.False(sensor.IsNull, $"the node keeps its sensor 0x{site:X8}");
                var buf = new EqsCognitiveBuffer { Count = (byte)points.Length, LastUpdateTick = tick };
                var span = buf.GetSpanRW();
                for (int i = 0; i < points.Length; i++)
                    span[i] = new EqsResult { PositionX = points[i].X, PositionY = points[i].Y, PositionZ = points[i].Z, Score = 1f - 0.1f * i };
                Repo.SetComponent(sensor, buf);
            }

            public FiringPositionMemory Memory => UnitMemory.Get<FiringPositionMemory>(Repo, Self);

            public void Sense(SensorChange kind) => Repo.GetComponentRW<RecentSenses>(Self).Record(kind, Time);

            /// <summary>Hides at <paramref name="hide"/>: answers the hide query and drives there.</summary>
            public void ToHide(Vector3 hide, params Vector3[] more)
            {
                Step();
                var all = new List<Vector3> { hide };
                all.AddRange(more);
                Answer(VehicleFightFromCoverNodes.HideSite, 5, all.ToArray());
                Assert.Equal(VehiclePhase.Hidden, RunUntil(VehiclePhase.Hidden));
                Step();   // hidden: the firing-point sensor is kept from here
            }

            /// <summary>The TKB: a T-72 (500 / 250 / 150), a technical (5 / 5 / 5), an RPG man (300 mm, aim 2 s).</summary>
            public void UseTkb()
            {
                var db = new Fdp.Toolkit.Tkb.TkbDatabase();
                var tank = new Fdp.Interfaces.TkbTemplate("T-72", TankType);
                tank.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 2500, ArmorFront = 500, ArmorSide = 250, ArmorRear = 150 });
                var technical = new Fdp.Interfaces.TkbTemplate("Technical", TechnicalType);
                technical.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 300, ArmorFront = 5, ArmorSide = 5, ArmorRear = 5 });
                var rpg = new Fdp.Interfaces.TkbTemplate("RPG man", RpgManType);
                rpg.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 100 });
                rpg.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Range = 300, Penetration = 300, DamagePerHit = 400, AimSeconds = 2f } } });
                db.Register(tank); db.Register(technical); db.Register(rpg);
                Repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
                Repo.AddComponent(Self, new TkbIdentity { TkbType = TankType });
                Repo.AddComponent(Self, new Health { Current = 2500, Max = 2500 });
                Repo.AddComponent(Enemy, new TkbIdentity { TkbType = RpgManType });
                Repo.AddComponent(Enemy, new Health { Current = 100, Max = 100 });
                Repo.AddComponent(Enemy, new WeaponState { Ammo = 4, MaxAmmo = 4 });
            }

            /// <summary>Moves the enemy (and the unit's memory of him) to <paramref name="at"/>.</summary>
            public void EnemyTo(Vector3 at)
            {
                Repo.GetComponentRW<SimTransform>(Enemy).Position = at;
                ref var mem = ref Repo.GetComponentRW<TargetMemory>(Self);
                mem.PositionsX[0] = at.X; mem.PositionsY[0] = at.Y; mem.PositionsZ[0] = at.Z;
            }
        }

        private static VehicleFightFromCoverParams Tank() => new()
        {
            WithdrawMode = WithdrawMode.Reverse, HideSecondsMin = 2f, HideSecondsMax = 2f,
        };

        private static VehicleFightFromCoverParams Technical() => new()
        {
            WithdrawMode = WithdrawMode.Scoot, HideSecondsMin = 2f, HideSecondsMax = 2f,
        };

        /// <summary>
        /// ⭐⭐⭐ <c>V4_R1</c> — THE CYCLE (a tank): to the hide, the gun TRACKING the threat while hidden, forward out to the firing point,
        /// <c>RoundsPerExposure</c> aimed rounds at the seen enemy, then REVERSE straight back into the SAME hide — the fire stopped, the gun
        /// tracking again; the exposure counts one use and one unit of heat on the FIRING point. 🔴 Red-proof: drop
        /// <c>reverseAllowed: true</c> in <c>Withdraw</c> and the last move is a forward drive.
        /// </summary>
        [Fact]
        public void V4_TheCycle_HidesExposesFiresAndReverses_ToTheSameHide()
        {
            var d = new Duel(Tank());
            d.Step();
            Assert.False(d.Sensor(VehicleFightFromCoverNodes.HideSite).IsNull, "the node points its own hide sensor at the threat");
            Assert.Equal(EnemyAt, d.Repo.GetComponentRO<EqsSensor>(d.Sensor(VehicleFightFromCoverNodes.HideSite)).ContextPoint1);   // D14
            Assert.Equal(FindVehicleHide.BlueprintId, d.Repo.GetComponentRO<EqsSensor>(d.Sensor(VehicleFightFromCoverNodes.HideSite)).BlueprintId);
            Assert.Equal(VehiclePhase.Choose, d.Ws.Phase);

            d.ToHide(HideA);
            Assert.True(d.Tracking, "hidden, the gun is kept on the threat (T4)");
            Assert.False(d.Sensor(VehicleFightFromCoverNodes.FiringSite).IsNull, "hidden, the node asks for a firing point");
            Assert.Equal(FindVehicleFiringPoint.BlueprintId, d.Repo.GetComponentRO<EqsSensor>(d.Sensor(VehicleFightFromCoverNodes.FiringSite)).BlueprintId);
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);

            Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging));
            Assert.False(d.Tracking);
            Assert.Equal(d.Enemy, Assert.Single(d.AimedAt));
            Assert.Equal(VehiclePhase.Hidden, d.RunUntil(VehiclePhase.Hidden));

            Assert.Equal(new[] { (HideA, false), (FiringA, false), (HideA, true) }, d.Moves);   // out FRONT first, back in REVERSE
            Assert.Equal(2, d.AimedRounds);
            Assert.False(d.Firing);
            d.Step();
            Assert.True(d.Tracking, "back in the hide, the gun tracks again");
            var m = d.Memory;
            Assert.Equal(1, m.Count);
            Assert.Equal(FiringA, new Vector3(m.X[0], m.Y[0], m.Z[0]));   // the FIRING point heats: that is where he saw me
            Assert.Equal(1, m.Uses[0]);
        }

        /// <summary>
        /// ⭐⭐ <c>V4_R2</c> — a tank fights <c>ExposuresPerPosition</c> (0 ⇒ 2 for Reverse) times from one hide, then moves to another at
        /// least <c>MinRelocateMetres</c> away — and its firing point there is the new hide's.
        /// </summary>
        [Fact]
        public void V4_ATank_ExposesTwiceFromOneHide_ThenRelocates()
        {
            var d = new Duel(Tank());
            d.ToHide(HideA, new Vector3(-12f, 3f, 0f), HideB);   // the second is too close to count as a relocation
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
            for (int exposure = 0; exposure < 2; exposure++)
            {
                Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging));
                Assert.Equal(VehiclePhase.Hidden, d.RunUntil(VehiclePhase.Hidden));
            }
            Assert.Equal(VehiclePhase.MoveToHide, d.RunUntil(VehiclePhase.MoveToHide));
            Assert.Equal((HideB, false), d.Moves[^1]);
            Assert.Equal(2, d.Ws.Exposures);
        }

        /// <summary>
        /// ⭐⭐⭐ <c>V4_R3</c> — A LIGHT VEHICLE SCOOTS (D-J): after its one exposure it drives FORWARD on to a NEW hide — never the old one,
        /// never in reverse — with the fire LEFT RUNNING for the drive (the weapon brain fires a <c>FireOnMove</c> mount and holds the others,
        /// D-L); the fire stops once hidden. 🔴 Red-proof: route Scoot through the reverse branch and the move is <c>(HideA, reverse)</c>.
        /// </summary>
        [Fact]
        public void V4_ATechnical_Scoots_ToANewHide_EveryTime()
        {
            var d = new Duel(Technical() with { RoundsPerExposure = 100, ExposeSeconds = 1f });   // the exposure ends on time, mid-fire
            d.ToHide(HideA, HideB);
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
            Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging));
            Assert.Equal(VehiclePhase.MoveToHide, d.RunUntil(VehiclePhase.MoveToHide));
            Assert.Equal((HideB, false), d.Moves[^1]);
            Assert.True(d.Firing, "scooting, the fire keeps running for the drive");
            Assert.Equal(VehiclePhase.Hidden, d.RunUntil(VehiclePhase.Hidden));
            Assert.False(d.Firing, "hidden, the fire stops");
            Assert.Equal(new[] { (HideA, false), (FiringA, false), (HideB, false) }, d.Moves);
            var m = d.Memory;
            Assert.Equal(1, m.Burned[0]);   // one use burns a scooting vehicle's firing point
        }

        /// <summary>
        /// ⭐⭐⭐ <c>V4_R4</c> — THE BUDGET IS THE AT GUNNER'S AIM TIME (§4): an RPG man on the tank's SIDE (300 mm through 250 ⇒ danger 1)
        /// with a 2 s aim cuts the exposure to 0.7 × 2 = 1.4 s of being SEEN by him; the same man in FRONT (300 vs 500 ⇒ 0.1) does not — the
        /// exposure runs to <c>ExposeSeconds</c>. 🔴 Red-proof: let <c>Budget</c> ignore <c>ThreatDanger</c> and the front case is cut too.
        /// </summary>
        [Fact]
        public void V4_TheBudget_IsTheAtGunnersAimTime()
        {
            double ExposedFor(Vector3 enemyAt)
            {
                var d = new Duel(Tank() with { RoundsPerExposure = 100, ExposeSeconds = 6f });
                d.UseTkb();
                d.EnemyTo(enemyAt);
                d.ToHide(HideA);
                d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
                Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging));
                double up = d.Time;
                Assert.Equal(VehiclePhase.Withdrawing, d.RunUntil(VehiclePhase.Withdrawing));
                return d.Time - up;
            }

            Assert.InRange(ExposedFor(new Vector3(0f, 80f, 0f)), 1.3, 1.7);    // on the side: 1.4 s of his sight
            Assert.InRange(ExposedFor(new Vector3(80f, 0f, 0f)), 5.8, 6.2);    // in front: he cannot hurt me — the full exposure
        }

        /// <summary>⭐ <c>V4_R4b</c> — the budget on its own: the dangerous man must be SEEN to count; nothing in sight ⇒ <c>ExposeSeconds</c>.</summary>
        [Fact]
        public void V4_TheBudget_CountsOnlyASeenDangerousThreat()
        {
            var d = new Duel(Tank());
            d.UseTkb();
            d.EnemyTo(new Vector3(0f, 80f, 0f));
            var p = VehicleFightFromCoverNodes.Effective(d.P, scoot: false);
            Assert.InRange(VehicleFightFromCoverNodes.Budget(in p, d.Repo, d.Self), 1.39f, 1.41f);

            // a rifleman in sight who aims in 0.5 s but cannot hurt my armour (danger 0.1) does NOT shorten it — 🔴 red-proof: drop the
            //   danger filter in Budget and it is 0.7 × 0.5 = 0.35 s (the cycle rail cannot see this: SeenSince filters on danger too)
            var db = (Fdp.Toolkit.Tkb.TkbDatabase)d.Repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
            var rifle = new Fdp.Interfaces.TkbTemplate("Rifleman", RiflemanType);
            rifle.AddDescriptor(new CombatPlatformDefDto { MaxHealth = 100 });
            rifle.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Range = 500, Penetration = 5, DamagePerHit = 10, AimSeconds = 0.5f } } });
            db.Register(rifle);
            var rifleman = d.Repo.CreateEntity();
            d.Repo.AddComponent(rifleman, new SimTransform { Position = new Vector3(60f, 0f, 0f), Rotation = Quaternion.Identity });
            d.Repo.AddComponent(rifleman, new TkbIdentity { TkbType = RiflemanType });
            d.Repo.AddComponent(rifleman, new Health { Current = 100, Max = 100 });
            d.Repo.AddComponent(rifleman, new WeaponState { Ammo = 30, MaxAmmo = 30 });
            ref var mem = ref d.Repo.GetComponentRW<TargetMemory>(d.Self);
            mem.EntityIds[1] = (long)rifleman.PackedValue;
            mem.Freshness[1] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
            mem.Modalities[1] = (byte)SensorModality.Visual;
            mem.PositionsX[1] = 60f;
            mem.Count = 2;
            ref var tracks = ref d.Repo.GetComponentRW<ActiveSensorTracks>(d.Self);
            tracks.EntityIds[1] = (long)rifleman.PackedValue;
            tracks.Modalities[1] = (byte)SensorModality.Visual;
            tracks.Count = 2;
            Assert.True(Fdp.Toolkit.Perception.SightNow.Sees(d.Repo, d.Self, rifleman), "the rifleman is in sight");
            Assert.InRange(VehicleFightFromCoverNodes.Budget(in p, d.Repo, d.Self), 1.39f, 1.41f);

            d.See(false);
            Assert.Equal(p.ExposeSeconds, VehicleFightFromCoverNodes.Budget(in p, d.Repo, d.Self));
        }

        /// <summary>⭐⭐ <c>V4_R5</c> — a NEAR MISS while exposed cuts the exposure at once: back in reverse, rounds left unfired.</summary>
        [Fact]
        public void V4_ANearMiss_CutsTheExposure()
        {
            var d = new Duel(Tank() with { RoundsPerExposure = 100 });
            d.ToHide(HideA);
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
            Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging));
            d.Step();
            d.Sense(SensorChange.NearMiss);
            d.Step();
            Assert.Equal(VehiclePhase.Withdrawing, d.Ws.Phase);
            Assert.Equal((HideA, true), d.Moves[^1]);
            Assert.True(d.AimedRounds < 10, "the fire was cut short");
        }

        /// <summary>
        /// ⭐⭐ <c>V4_R6</c> — GUIDING HOLDS THE FIRING POINT (Missiles §4): a command missile in flight keeps the vehicle out past
        /// <c>ExposeSeconds</c> — and a hit still sends it back. 🔴 Red-proof: drop <c>!guiding</c> and it withdraws on time.
        /// </summary>
        [Fact]
        public void V4_Guiding_Holds_TheFiringPoint()
        {
            var d = new Duel(Tank() with { RoundsPerExposure = 100, ExposeSeconds = 1f });
            d.ToHide(HideA);
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
            Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging));
            d.Guiding = true;
            d.Run(3);
            Assert.Equal(VehiclePhase.Engaging, d.Ws.Phase);
            d.Sense(SensorChange.Hit);
            d.Step();
            Assert.Equal(VehiclePhase.Withdrawing, d.Ws.Phase);
        }

        /// <summary>
        /// ⭐⭐ <c>V4_R7</c> — T5: a HEAVY WEAPON heard since the exposure began, toward the threat (a launch from a window nobody saw), cuts it;
        /// the same sound heard before the exposure, or off to the side, does not.
        /// </summary>
        [Fact]
        public void V4_ALaunchHeard_TowardTheThreat_CutsTheExposure()
        {
            VehiclePhase After(Vector3 heardAt, bool beforeTheExposure)
            {
                var d = new Duel(Tank() with { RoundsPerExposure = 100, ExposeSeconds = 6f });
                d.ToHide(HideA);
                d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
                Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging));
                ref var mem = ref d.Repo.GetComponentRW<TargetMemory>(d.Self);
                mem.EntityIds[1] = -7;
                mem.Anonymous[1] = 1;
                mem.Freshness[1] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
                mem.Modalities[1] = (byte)SensorModality.Acoustic;
                mem.SourceClass[1] = (byte)SoundSourceClass.HeavyWeapon;
                mem.PositionsX[1] = heardAt.X; mem.PositionsY[1] = heardAt.Y; mem.PositionsZ[1] = heardAt.Z;
                mem.LastSeenTick[1] = beforeTheExposure ? d.Ws.ExposedTick - 1 : d.Ws.ExposedTick;
                mem.Count = 2;
                d.Step();
                return d.Ws.Phase;
            }

            Assert.Equal(VehiclePhase.Withdrawing, After(new Vector3(70f, 15f, 0f), beforeTheExposure: false));
            Assert.Equal(VehiclePhase.Engaging, After(new Vector3(70f, 15f, 0f), beforeTheExposure: true));
            Assert.Equal(VehiclePhase.Engaging, After(new Vector3(0f, 70f, 0f), beforeTheExposure: false));
        }

        /// <summary>
        /// ⭐⭐ <c>V4_R8</c> — WATCHING: at the firing point with nothing in sight, the vehicle observes for <c>WatchSeconds</c> and withdraws
        /// without a shot; seen while watching, it fires.
        /// </summary>
        [Fact]
        public void V4_NothingInSight_Watches_ThenWithdraws_OrFiresWhenHeShowsHimself()
        {
            var d = new Duel(Tank() with { WatchSeconds = 2f }, seen: false);
            d.ToHide(HideA);
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
            Assert.Equal(VehiclePhase.Watching, d.RunUntil(VehiclePhase.Watching));
            double up = d.Time;
            Assert.Equal(VehiclePhase.Hidden, d.RunUntil(VehiclePhase.Hidden));
            Assert.InRange(d.Time - up, 1.9, 2.5);
            Assert.Equal(0, d.AimedRounds);

            Assert.Equal(VehiclePhase.Watching, d.RunUntil(VehiclePhase.Watching));
            d.See(true);
            d.Step();
            Assert.Equal(VehiclePhase.Engaging, d.Ws.Phase);
        }

        /// <summary>⭐ <c>V4_R9</c> — under fire or reloading the vehicle stays in its hide past its wait.</summary>
        [Fact]
        public void V4_UnderFireOrReloading_StaysHidden()
        {
            var d = new Duel(Tank());
            d.ToHide(HideA);
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
            d.Repo.GetComponentRW<WeaponState>(d.Self).ReloadSecondsRemaining = 99f;
            d.Run(4);
            Assert.Equal(VehiclePhase.Hidden, d.Ws.Phase);
            d.Repo.GetComponentRW<WeaponState>(d.Self).ReloadSecondsRemaining = 0f;
            d.Sense(SensorChange.NearMiss);
            d.Run(2);
            Assert.Equal(VehiclePhase.Hidden, d.Ws.Phase);
            Assert.Equal(VehiclePhase.Exposing, d.RunUntil(VehiclePhase.Exposing, 2.0));
        }

        /// <summary>
        /// ⭐⭐ <c>V4_R10</c> — LEAVING the node: both sensors go, the tracking and the move stop, the claim is freed — and the position
        /// memory stays (a new run does not reuse a burned firing point).
        /// </summary>
        [Fact]
        public void V4_Leaving_ReleasesEverything_ButTheMemory()
        {
            var d = new Duel(Tank());
            d.ToHide(HideA);
            Assert.True(d.Tracking);
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
            Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging));
            VehicleFightFromCoverNodes.Deactivate_VehicleFightFromCover(ref d.P, ref d.Ws, d.Self, d.Repo);
            d.Repo.FlushCommandBuffers();
            Assert.True(d.Sensor(VehicleFightFromCoverNodes.HideSite).IsNull);
            Assert.True(d.Sensor(VehicleFightFromCoverNodes.FiringSite).IsNull);
            Assert.False(d.Firing);
            Assert.False(d.Tracking);
            Assert.Equal(0d, d.Repo.GetComponentRO<CoverClaim>(d.Self).Until);
            Assert.Equal(VehiclePhase.Choose, d.Ws.Phase);
            Assert.Equal(1, d.Memory.Count);
        }

        /// <summary>⭐ <c>V4_R11</c> — nothing remembered: Success.</summary>
        [Fact]
        public void V4_NothingRemembered_Succeeds()
        {
            var d = new Duel(Tank());
            d.Step();
            d.Repo.GetComponentRW<TargetMemory>(d.Self).Count = 0;
            Assert.Equal(NodeStatus.Success, d.Step());
            Assert.True(d.Sensor(VehicleFightFromCoverNodes.HideSite).IsNull);
        }

        /// <summary>
        /// ⭐⭐ <c>V4_R12</c> — D-J, <c>Auto</c> reads the hull: a T-72 (front 500 ≥ 2 × side 250) reverses, a technical (5 / 5) scoots, an
        /// untyped vehicle scoots; an authored mode wins.
        /// </summary>
        [Fact]
        public void V4_AutoWithdraw_ReversesATank_ScootsATechnical()
        {
            var d = new Duel(default);
            d.UseTkb();
            Assert.False(VehicleFightFromCoverNodes.Scoots(default, d.Repo, d.Self));
            d.Repo.GetComponentRW<TkbIdentity>(d.Self).TkbType = TechnicalType;
            Assert.True(VehicleFightFromCoverNodes.Scoots(default, d.Repo, d.Self));
            Assert.False(VehicleFightFromCoverNodes.Scoots(new VehicleFightFromCoverParams { WithdrawMode = WithdrawMode.Reverse }, d.Repo, d.Self));
            Assert.Equal(1, VehicleFightFromCoverNodes.Effective(default, scoot: true).ExposuresPerPosition);
            Assert.Equal(2, VehicleFightFromCoverNodes.Effective(default, scoot: false).ExposuresPerPosition);
        }
    }
}
