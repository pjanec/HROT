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
            /// <summary>⭐ CE-2205 — the fake follower never arrives (live: a hull orbits a point inside its turning circle).</summary>
            public bool Orbits;
            /// <summary>⭐ CE-2230 — the fake follower DRIVES: the hull moves toward the move's destination at its speed each step
            /// (instead of arriving on the next step).</summary>
            public bool Drives;
            /// <summary>⭐ CE-2230 — the crest: the unit sees the enemy exactly while its X is at least this (null = <see cref="See"/> rules).</summary>
            public float? SeesFromX;
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
                if (SeesFromX is float crest) See(Repo.GetComponentRO<SimTransform>(Self).Position.X >= crest);
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
                if (Orbits) return;
                if (Drives)
                {
                    MoveToParams mp;
                    fixed (byte* src = loco.Params) mp = *(MoveToParams*)src;
                    ref var tf = ref Repo.GetComponentRW<SimTransform>(Self);
                    var at = new Vector2(tf.Position.X, tf.Position.Y);
                    var to = new Vector2(mp.Destination.X, mp.Destination.Y);
                    float step = mp.Speed * 0.1f;
                    if ((to - at).Length() <= MathF.Max(mp.ArrivalRadius, step)) { tf.Position = new Vector3(to, tf.Position.Z); loco.Status = NodeStatus.Success; }
                    else { at += Vector2.Normalize(to - at) * step; tf.Position = new Vector3(at, tf.Position.Z); }
                    return;
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
                ref var row = ref Repo.GetComponentRW<ActionStatus>(Self).Weapon;
                if (Masked) { row.Reason = ActionReason.Masked; row.At = Time; return; }   // ⭐ CE-2218 — the muzzle line runs into a wall: held
                row.Reason = Guiding ? ActionReason.Guiding : ActionReason.Firing;
                if (Guiding) return;   // a round in flight: the next is not granted
                _fired++;
                AimedRounds++;
                if (rounds > 0 && _fired >= rounds) ch.Status = NodeStatus.Success;
            }

            /// <summary>⭐ CE-2218 — the weapon holds Masked (its round would stop in front of the muzzle).</summary>
            public bool Masked;

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

        /// <summary>⭐ CE-2230 — the hill attack's run: the hide given (the baseline slot at the origin), a lane from (40, 0) east,
        /// one round, one exposure, reverse.</summary>
        private static VehicleFightFromCoverParams Hill() => new()
        {
            WithdrawMode = WithdrawMode.Reverse, HideSecondsMin = 1f, HideSecondsMax = 1f, RoundsPerExposure = 1, MaxExposures = 1,
            UseGivenHide = 1, GivenHide = Vector2.Zero, LaneStart = new Vector2(40f, 0f), LaneDir = new Vector2(1f, 0f),
            ApproachSpeed = 10f, CreepSpeed = 5f,
        };

        private static Duel HillDuel(float seesFromX) => new(Hill(), seen: false) { Drives = true, SeesFromX = seesFromX };

        /// <summary>
        /// ⭐⭐⭐ <c>CE-2230</c> HA-2 — THE CREST: on the lane the unit stops the FIRST tick it sees its target — here on the APPROACH, 20 m
        /// short of the lane start — and fires from there (hull-down: only the turret clears the crest, R-246). 📌 The node it replaces
        /// looked only once near the slot. 🔒 R-2022. 🔴 Red-proof: check the sight only while creeping and it drives on past x = 20.
        /// </summary>
        /// <summary>
        /// ⭐ <c>CE-2230</c> H-5 — the WAVE's go time: in its hide well before, the hull does not drive out before <c>HoldUntil</c> (the
        /// commander's one go time for the pair), and then goes at once. 🔴 Red-proof: drop the hold and it leaves at the hide timer (~1 s).
        /// </summary>
        [Fact]
        public void CE2230_H6_FromAHideBesideTheLane_TheHullCrestsOnTheLane_NotBesideIt()
        {
            // ⭐ CE-2230 H-6 — the hide 20 m beside the lane: the approach is a diagonal, but the creep runs ON the lane, so the hull is
            //   where the commander put it when it first sees (live: up to ~10 m off, which undid "away from the last pair").
            //   🔴 Red-proof: start the creep 15 m short, straight on from the diagonal, and the hull stops ~6 m beside the lane.
            var hill = Hill();
            hill.GivenHide = new Vector2(0f, 20f);
            var d = new Duel(hill, seen: false) { Drives = true, SeesFromX = 60f };
            d.Repo.SetComponent(d.Self, new SimTransform { Position = new Vector3(0f, 20f, 0f), Rotation = Quaternion.Identity });
            Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging, 40));
            var at = d.Repo.GetComponentRO<SimTransform>(d.Self).Position;
            Assert.InRange(at.X, 60f, 61.5f);
            Assert.InRange(at.Y, -1.5f, 1.5f);
        }

        [Fact]
        public void CE2230_H5_TheHullWaitsInItsHide_UntilTheWavesGoTime()
        {
            var d = HillDuel(seesFromX: 1000f);
            d.P.HoldUntil = 8.0;
            Assert.Equal(VehiclePhase.Hidden, d.RunUntil(VehiclePhase.Hidden, 5));
            Assert.True(d.Time < 3.0, $"in the hide at {d.Time:F1} s");
            Assert.Equal(VehiclePhase.Exposing, d.RunUntil(VehiclePhase.Exposing, 10));
            Assert.InRange(d.Time, 8.0, 8.3);   // out at the go time, not at the hide timer
        }

        [Fact]
        public void CE2230_TheLane_StopsTheFirstTickItSeesItsTarget_ApproachIncluded()
        {
            var d = HillDuel(seesFromX: 20f);
            Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging, 20));
            float x = d.Repo.GetComponentRO<SimTransform>(d.Self).Position.X;
            Assert.InRange(x, 20f, 21.5f);   // one approach step (1 m) at most past the first sight
            Assert.Equal(d.Enemy, Assert.Single(d.AimedAt));
            Assert.True(EqsChildSensor.Find(d.Repo, d.Self, VehicleFightFromCoverNodes.HideSite).IsNull, "a given hide asks no hide query");
            Assert.True(d.Sensor(VehicleFightFromCoverNodes.FiringSite).IsNull, "a lane asks no firing-point query");
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-2230</c> HA-3 / 🔒 R-2022 — ONE ROUND, THEN BACK: the creep (past the lane start) stops on the sight, one aimed round,
        /// then a REVERSE to the given hide, and the run answers Success (the commander's next pair goes). 🔴 Red-proof: drop the
        /// <c>MaxExposures</c> check and the node is still Running, hidden, after the reverse.
        /// </summary>
        [Fact]
        public void CE2230_OneRound_ThenReversesToTheGivenHide_AndTheRunSucceeds()
        {
            var d = HillDuel(seesFromX: 55f);
            Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging, 30));
            Assert.InRange(d.Repo.GetComponentRO<SimTransform>(d.Self).Position.X, 55f, 56f);
            NodeStatus status = NodeStatus.Running;
            for (int i = 0; i < 400 && status == NodeStatus.Running; i++) status = d.Step();
            Assert.Equal(NodeStatus.Success, status);
            Assert.Equal(1, d.AimedRounds);
            Assert.Equal((new Vector3(0f, 0f, 0f), true), d.Moves[^1]);   // backed straight into the hide
            Assert.True(new Vector2(d.Repo.GetComponentRO<SimTransform>(d.Self).Position.X, 0f).Length() <= VehicleFightFromCoverNodes.ArrivalRadius);
        }

        /// <summary>
        /// ⭐ <c>CE-2230</c> HA-2 — NO SIGHT WITHIN THE CAP: the creep goes no further than <c>MaxOvershootMetres</c> past the lane start
        /// (CE-174: a target never in sight is not chased), backs into the hide, and the run still ends. 🔴 Red-proof: drop the cap and
        /// the hull creeps on past x = 90.
        /// </summary>
        [Fact]
        public void CE2230_NoSightWithinTheCap_BacksIntoTheHide_AndTheRunEnds()
        {
            var d = HillDuel(seesFromX: 1000f);
            NodeStatus status = NodeStatus.Running;
            float furthest = 0f;
            for (int i = 0; i < 800 && status == NodeStatus.Running; i++)
            {
                status = d.Step();
                furthest = MathF.Max(furthest, d.Repo.GetComponentRO<SimTransform>(d.Self).Position.X);
            }
            Assert.Equal(NodeStatus.Success, status);
            Assert.InRange(furthest, 89f, 91.5f);   // the lane start 40 + the 50 m cap
            Assert.Equal(0, d.AimedRounds);
            Assert.True(d.Moves[^1].Reverse, "back into the hide in reverse");
        }

        /// <summary>
        /// ⭐ <c>CE-2230</c> / 🔒 R-2022 — the target gone mid-run (killed by the other tank of the pair, or forgotten): with a GIVEN
        /// hide the unit still backs into it before the run ends — never left standing on the crest. 🔴 Red-proof: drop the given-hide
        /// arm of "nothing left to fight" and the run ends out on the lane.
        /// </summary>
        [Fact]
        public void CE2230_TheTargetGoneMidRun_StillBacksIntoTheHide()
        {
            var d = HillDuel(seesFromX: 1000f);
            Assert.Equal(VehiclePhase.Exposing, d.RunUntil(VehiclePhase.Exposing, 20));
            d.Run(3.0);   // out on the lane, approaching
            Assert.True(d.Repo.GetComponentRO<SimTransform>(d.Self).Position.X > 10f);
            d.Repo.GetComponentRW<TargetMemory>(d.Self).Count = 0;   // nothing left to fight
            NodeStatus status = NodeStatus.Running;
            for (int i = 0; i < 400 && status == NodeStatus.Running; i++) status = d.Step();
            Assert.Equal(NodeStatus.Success, status);
            Assert.True(d.Moves[^1].Reverse, "back into the hide in reverse");
            Assert.True(d.Repo.GetComponentRO<SimTransform>(d.Self).Position.X <= VehicleFightFromCoverNodes.ArrivalRadius,
                $"ended in the hide (x = {d.Repo.GetComponentRO<SimTransform>(d.Self).Position.X:0.0})");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2351</c> (Exposure Cycle §4a) — THE SENSORS KEEP TO THE NAMED FIGHT: a sound heard 120 m from where the threat is
        /// remembered, newer than the sighting, is another fight — the hide sensor stays on the threat's spot; one heard 8 m from it is
        /// the threat moving — the sensor follows it (D14). 📐 Live <c>ua-javelin-lock</c>: the other vignette's launch and impact pulled
        /// the tank's hide and firing queries 160 m away from the Javelin team its attack order named. 🔴 Red-proof: pass no radius to
        /// <c>SensorAim</c> in <c>KeepSensors</c> and the first half points the sensor at the far sound.
        /// </summary>
        [Fact]
        public void CE2351_ASoundFromAnotherFight_DoesNotPullTheSensors_ASoundNearTheThreatDoes()
        {
            Vector3 HideAimAfterHearing(Vector3 heard)
            {
                var d = new Duel(Tank());
                ref var mem = ref d.Repo.GetComponentRW<TargetMemory>(d.Self);
                mem.LastSeenTick[0] = 10;
                mem.EntityIds[1] = -7;
                mem.Anonymous[1] = 1;
                mem.Freshness[1] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
                mem.Modalities[1] = (byte)SensorModality.Acoustic;
                mem.PositionsX[1] = heard.X; mem.PositionsY[1] = heard.Y; mem.PositionsZ[1] = heard.Z;
                mem.LastSeenTick[1] = 50;
                mem.Count = 2;
                d.Step();
                return d.Repo.GetComponentRO<EqsSensor>(d.Sensor(VehicleFightFromCoverNodes.HideSite)).ContextPoint1;
            }

            Assert.Equal(EnemyAt, HideAimAfterHearing(EnemyAt + new Vector3(0f, 120f, 0f)));   // another fight
            var near = EnemyAt + new Vector3(5f, 6f, 0f);
            Assert.Equal(near, HideAimAfterHearing(near));                                     // the threat, moved
        }

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

            d.See(false);   // unseen on the drive in — seen, the drive would fire too (CE-2219, its own rail)
            d.ToHide(HideA);
            d.See(true);
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
        /// ⭐ <c>CE-2218</c> (backend's handoff: "a shooter held <c>Masked</c> should move") — at a firing point whose gun line runs into a
        /// wall or bank the weapon holds <c>Masked</c>: the exposure ends at once, the firing point is BURNED (it cannot shoot that target
        /// from there), and the next exposure asks for another. 🔴 Red-proof: without the node's masked check it stays out for the whole
        /// exposure and the point is only heated once.
        /// </summary>
        [Fact]
        public void CE2218_AMaskedGun_BurnsTheFiringPoint_AndGoesBackIn()
        {
            var d = new Duel(Tank() with { ExposeSeconds = 20f });
            d.ToHide(HideA);
            int roundsBefore = d.AimedRounds;   // the drive in may already have fired (CE-2219)
            d.Masked = true;
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
            Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging));
            double out_ = d.Time;
            Assert.NotEqual(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Withdrawing, 3));
            Assert.True(d.Time - out_ < 2.0, $"a masked gun goes back in at once ({d.Time - out_:0.0} s out)");
            Assert.Equal(roundsBefore, d.AimedRounds);   // nothing spent out there
            var m = d.Memory;
            Assert.True(m.Heat[0] >= VehicleFightFromCoverNodes.Rules(Tank()).BurnHeat, $"the firing point is burned (heat {m.Heat[0]:0.##})");
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
        /// ⭐ <c>CE-2229</c> — a hide that no firing point near it can fight from is given up (§3 state diagram: <i>Hidden → Choose: every
        /// firing point near this hide burned</i>) — for ANOTHER hide, even before the first exposure. 📌 Live <c>ua-technical-scoot</c>: the
        /// HMG technical's hide at (285, 230) saw no line to the tank; it gave the hide up, re-picked the SAME hide (the relocation rule only
        /// fired after an exposure), arrived at once and sat there 100 s. 🔴 Red-proof: <c>relocating: ws.Exposures &gt; 0</c> alone and
        /// the last move is <c>(HideA, false)</c> again.
        /// </summary>
        [Fact]
        public void CE2229_AHideWithNoFiringPoint_IsLeft_ForAnotherHide_BeforeAnyExposure()
        {
            var p = Technical();
            var d = new Duel(p);
            var nearA = new Vector3(-10f, 5f, 0f);   // outside the burn's 3 m match, inside the 15 m relocation
            d.ToHide(HideA, nearA, HideB);
            // the firing-point sensor never answers: nothing near HideA sees the threat
            Assert.Equal(VehiclePhase.MoveToHide, d.RunUntil(VehiclePhase.MoveToHide, GiveUpSeconds(p)));
            Assert.Equal((HideB, false), d.Moves[^1]);
            Assert.Equal(0, d.Ws.Exposures);
        }

        private static double GiveUpSeconds(VehicleFightFromCoverParams p) => 2 + VehicleFightFromCoverNodes.Effective(p, true).NoCoverSeconds + 2;

        /// <summary>
        /// ⭐ <c>CE-2229</c> — the blind hide given up is also BURNED, so two blind hides are not taken in turn: A, then B, then C — never
        /// back to A. 🔴 Red-proof: drop the <c>PositionHeat.Add</c> on the hide and the third move is <c>(HideA, false)</c>.
        /// </summary>
        [Fact]
        public void CE2229_TwoBlindHides_AreNotTakenInTurn()
        {
            var p = Technical();
            var d = new Duel(p);
            var hideC = new Vector3(-10f, 60f, 0f);
            d.ToHide(HideA, HideB, hideC);
            Assert.Equal(VehiclePhase.MoveToHide, d.RunUntil(VehiclePhase.MoveToHide, GiveUpSeconds(p)));
            Assert.Equal((HideB, false), d.Moves[^1]);
            Assert.Equal(VehiclePhase.Hidden, d.RunUntil(VehiclePhase.Hidden));
            d.Answer(VehicleFightFromCoverNodes.HideSite, 9, HideA, HideB, hideC);   // the hide sensor still ranks A first
            Assert.Equal(VehiclePhase.MoveToHide, d.RunUntil(VehiclePhase.MoveToHide, GiveUpSeconds(p)));
            Assert.Equal((hideC, false), d.Moves[^1]);
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

        /// <summary>
        /// ⭐⭐ <c>CE-2215</c> — a dangerous threat I do NOT see still bounds my exposure when it can see me: the RPG man on my side, not in
        /// my tracks, with a clear line from where I remember him ⇒ 1.4 s; a wall between us ⇒ the full exposure. 📐 Live <c>ua-javelin-lock</c>:
        /// the tank did not see the Javelin gunner it knew of, exposed for 8 s, and gave him his 4 s lock.
        /// 🔴 Red-proof (mutant): <c>CanSeeMe</c> = <c>SightNow.Sees</c> only ⇒ the open case is 6 s ⇒ red.
        /// </summary>
        [Fact]
        public void CE2215_AnUnseenThreat_ThatCanSeeMe_BoundsTheExposure_AWallBetweenUsDoesNot()
        {
            var d = new Duel(Tank());
            d.UseTkb();
            d.EnemyTo(new Vector3(0f, 80f, 0f));
            d.See(false);
            var p = VehicleFightFromCoverNodes.Effective(d.P, scoot: false);
            d.Repo.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainWorld { BoundsMin = new Vector2(-200, -200), BoundsMax = new Vector2(200, 200) });
            Assert.InRange(VehicleFightFromCoverNodes.Budget(in p, d.Repo, d.Self), 1.39f, 1.41f);

            var wall = new[] { new Vector2(-10f, 40f), new Vector2(10f, 40f), new Vector2(10f, 41f), new Vector2(-10f, 41f) };
            d.Repo.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainWorld
            {
                BoundsMin = new Vector2(-200, -200), BoundsMax = new Vector2(200, 200),
                Prisms = new[] { new Fdp.Toolkit.Terrain.TerrainPrism { Kind = Fdp.Toolkit.Terrain.TerrainPrismKind.Wall, Footprint = wall, BaseZ = 0f, TopZ = 6f,
                                                                       Min = new Vector2(-10f, 40f), Max = new Vector2(10f, 41f) } },
            });
            Assert.Equal(p.ExposeSeconds, VehicleFightFromCoverNodes.Budget(in p, d.Repo, d.Self));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2216</c> — the DRIVE OUT is part of the exposure: seen by the RPG man on my side (2 s aim ⇒ a 1.4 s budget) while still
        /// driving to the firing point, I turn back to the hide once the budget is spent, before I ever reach it. 📐 Live <c>ua-javelin-lock</c>:
        /// Javelin A saw the tank 3 s before its firing point and launched as it arrived — the clock started only at the point.
        /// 🔴 Red-proof (mutant): no clock on the drive out ⇒ it never withdraws while the move runs ⇒ red.
        /// </summary>
        [Fact]
        public void CE2216_SeenOnTheDriveOut_TheBudgetRunsOut_BackToTheHide_BeforeTheFiringPoint()
        {
            var d = new Duel(Tank() with { RoundsPerExposure = 100, ExposeSeconds = 6f });
            d.UseTkb();
            d.EnemyTo(new Vector3(0f, 80f, 0f));
            d.ToHide(HideA);
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
            d.Orbits = true;                                                 // the drive out takes as long as it takes
            Assert.Equal(VehiclePhase.Exposing, d.RunUntil(VehiclePhase.Exposing));
            double outAt = d.Time;
            Assert.Equal(VehiclePhase.Withdrawing, d.RunUntil(VehiclePhase.Withdrawing, 10));
            Assert.InRange(d.Time - outAt, 1.3, 1.8);
            Assert.Equal(HideA, d.Moves[^1].Item1);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2219</c> — SURPRISED IN THE OPEN, THE GUN FIGHTS ON THE WAY TO THE HIDE: while the hull drives to its first hide it fires
        /// at the threat it SEES, and only tracks him once it does not. 📌 Live <c>ua-rpg-window</c>: an IFV drove 6 s with the gun only
        /// tracking the RPG man in the window and died without a round. 🔴 Red-proof: drop the fire step from <c>MoveToHide</c> and the
        /// drive is silent.
        /// </summary>
        [Fact]
        public void CE2219_DrivingToItsHide_ItFiresAtTheThreatItSees_AndTracksHimWhenItDoesNot()
        {
            var d = new Duel(Tank());
            d.Orbits = true;                                                 // the drive to the first hide takes as long as it takes
            d.Step();
            d.Answer(VehicleFightFromCoverNodes.HideSite, 5, HideA);
            Assert.Equal(VehiclePhase.MoveToHide, d.RunUntil(VehiclePhase.MoveToHide));
            d.Run(1.0);
            Assert.Equal(VehiclePhase.MoveToHide, d.Ws.Phase);
            Assert.True(d.Firing, "seen on the way, the gun fires");
            Assert.Contains(d.Enemy, d.AimedAt);
            Assert.True(d.AimedRounds > 0);
            d.See(false);
            d.Run(0.5);
            Assert.False(d.Firing, "out of sight, the fire stops");
            Assert.True(d.Tracking, "and the gun stays on him");
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
        /// ⭐⭐ <c>CE-2202</c> — A BODY IS NOT A THREAT. The one remembered enemy is knocked out (Health 0 — the body stays in the world and in
        /// memory, CE-267 / CE-466) while still in sight: the cycle ends (Success) instead of guarding the corpse. With a second, live enemy
        /// remembered (an old memory — it scores nothing, so the ranking's top is not him), that one is the threat. 📌 Live <c>ua-ifv-standoff</c>: the IFV stood over
        /// a dead RPG man while two live ones walked up to it. ✅ Red-proof: keeping the dead current ⇒ Running / the corpse.
        /// </summary>
        [Fact]
        public void CE2202_ADeadThreat_IsNotFought_ALiveOneRemembered_Is()
        {
            var d = new Duel(Tank());
            d.Repo.AddComponent(d.Enemy, new Health { Current = 100f, Max = 100f });
            Assert.Equal(NodeStatus.Running, d.Step());
            Assert.Equal(d.Enemy, d.Ws.Threat);
            d.Repo.GetComponentRW<Health>(d.Enemy).Current = 0f;
            Assert.Equal(NodeStatus.Success, d.Step());

            var e = new Duel(Tank());
            e.Repo.AddComponent(e.Enemy, new Health { Current = 100f, Max = 100f });
            var live = e.Repo.CreateEntity();
            e.Repo.AddComponent(live, new SimTransform { Position = new Vector3(60f, 40f, 0f), Rotation = Quaternion.Identity });
            e.Repo.AddComponent(live, new Health { Current = 100f, Max = 100f });
            ref var mem = ref e.Repo.GetComponentRW<TargetMemory>(e.Self);
            mem.EntityIds[1] = (long)live.PackedValue; mem.Modalities[1] = (byte)SensorModality.Visual;
            mem.Freshness[1] = 0f;   // an OLD memory: it scores nothing, so only the live-fallback (not the ranking) can find him
            mem.PositionsX[1] = 60f; mem.PositionsY[1] = 40f; mem.Count = 2;
            Assert.Equal(NodeStatus.Running, e.Step());
            e.Repo.GetComponentRW<Health>(e.Enemy).Current = 0f;
            Assert.Equal(NodeStatus.Running, e.Step());
            Assert.Equal(live, e.Ws.Threat);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2233</c> (CIV-0) — A NEUTRAL IS NOT A THREAT TO FIGHT. The one remembered contact is a civilian (side Neutral) in sight: the
        /// cycle has no threat (Success) — it does not hunt him. With a hostile also remembered (an old memory, as in CE-2202), the hostile is
        /// the threat even though the civilian is the ranking's top. ✅ Red-proof: <c>TopThreat</c>'s Engageable reduced to
        /// <c>CombatLife.IsAlive</c> ⇒ the civilian is the threat ⇒ red.
        /// </summary>
        [Fact]
        public void CE2233_ANeutralIsNotFought_AHostileRemembered_Is()
        {
            var d = new Duel(Tank());
            if (!d.Repo.IsComponentTypeRegistered<Fdp.Core.EntityInfo>()) d.Repo.RegisterComponent<Fdp.Core.EntityInfo>();
            d.Repo.AddComponent(d.Enemy, new Health { Current = 100f, Max = 100f });
            d.Repo.AddComponent(d.Enemy, new Fdp.Core.EntityInfo { ForceId = Fdp.Core.ForceId.Neutral });
            Assert.Equal(NodeStatus.Success, d.Step());

            var e = new Duel(Tank());
            if (!e.Repo.IsComponentTypeRegistered<Fdp.Core.EntityInfo>()) e.Repo.RegisterComponent<Fdp.Core.EntityInfo>();
            e.Repo.AddComponent(e.Enemy, new Health { Current = 100f, Max = 100f });
            e.Repo.AddComponent(e.Enemy, new Fdp.Core.EntityInfo { ForceId = Fdp.Core.ForceId.Neutral });
            var hostile = e.Repo.CreateEntity();
            e.Repo.AddComponent(hostile, new SimTransform { Position = new Vector3(60f, 40f, 0f), Rotation = Quaternion.Identity });
            e.Repo.AddComponent(hostile, new Health { Current = 100f, Max = 100f });
            e.Repo.AddComponent(hostile, new Fdp.Core.EntityInfo { ForceId = Fdp.Core.ForceId.Hostile });
            ref var mem = ref e.Repo.GetComponentRW<TargetMemory>(e.Self);
            mem.EntityIds[1] = (long)hostile.PackedValue; mem.Modalities[1] = (byte)SensorModality.Visual;
            mem.Freshness[1] = 0f;
            mem.PositionsX[1] = 60f; mem.PositionsY[1] = 40f; mem.Count = 2;
            Assert.Equal(NodeStatus.Running, e.Step());
            Assert.Equal(hostile, e.Ws.Threat);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2205</c> — A HULL WITHIN ITS ARRIVAL RADIUS HAS ARRIVED. 📌 Live <c>ua-javelin-lock</c>: an M1 sent to a cover point 2.3 m
        /// off (inside its ~5 m turning circle) orbited it for 60 s and the cycle never reached Hidden. With a follower that never arrives:
        /// a hide 2.3 m off is reached (Hidden, the move stopped); one 10 m off is still being driven to. ✅ Red-proof: the old 2 m radius,
        /// or the move's own answer only ⇒ red.
        /// </summary>
        [Fact]
        public void CE2205_AHideInsideTheArrivalRadius_IsReached_EvenWhenTheFollowerNeverArrives()
        {
            var d = new Duel(Tank()) { Orbits = true };
            d.Step();
            d.Answer(VehicleFightFromCoverNodes.HideSite, 5, new Vector3(-2.2f, 0.8f, 0f));
            Assert.Equal(VehiclePhase.Hidden, d.RunUntil(VehiclePhase.Hidden, 5));
            Assert.NotEqual(NavigationConstants.ActionIdMoveTo, d.Repo.GetComponentRO<LocomotionChannel>(d.Self).ActiveAction);   // the move stopped

            var far = new Duel(Tank()) { Orbits = true };
            far.Step();
            far.Answer(VehicleFightFromCoverNodes.HideSite, 5, HideA);
            far.Run(5);
            Assert.Equal(VehiclePhase.MoveToHide, far.Ws.Phase);
        }

        /// <summary>The hull's capability bits (what <c>CombatLife.ApplyHitCapabilities</c> leaves after a mobility / firepower kill).</summary>
        private static void Caps(Duel d, ActorCapabilities caps)
        {
            if (!d.Repo.HasComponent<ActorCapabilityState>(d.Self)) d.Repo.AddComponent(d.Self, new ActorCapabilityState { Capabilities = caps });
            else d.Repo.GetComponentRW<ActorCapabilityState>(d.Self).Capabilities = caps;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2196</c> — no hide answers for <c>NoCoverSeconds</c>: the vehicle FIGHTS FROM WHERE IT STANDS (was: the node failed,
        /// and with a target named <c>LandPlatformAttack</c> hung) — aimed fire at the seen enemy, no drive; the moment a hide answers it
        /// goes there. ✅ Red-proof: the old Failure ⇒ red.
        /// </summary>
        [Fact]
        public void CE2196_NoHide_FightsFromWhereItStands_ThenTakesAHideWhenOneAnswers()
        {
            var d = new Duel(Tank());
            d.Run(VehicleFightFromCoverNodes.Defaults.NoCoverSeconds + 1);
            Assert.Equal(VehiclePhase.StandAndFight, d.Ws.Phase);
            Assert.Equal(NodeStatus.Running, d.Step());
            Assert.True(d.Firing, "it fires at the seen enemy from where it stands");
            Assert.Equal(d.Enemy, Assert.Single(d.AimedAt));
            Assert.Empty(d.Moves);

            d.Answer(VehicleFightFromCoverNodes.HideSite, 9, HideA);
            d.Step();
            Assert.Equal(VehiclePhase.MoveToHide, d.Ws.Phase);
            Assert.Equal(HideA, Assert.Single(d.Moves).To);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2197</c> — a MOBILITY kill (backend's hit location S2: the running gear or engine — <c>CanMove</c> stripped) in its
        /// hide: it never drives again, it fights from where it stands; a hide answer does not move it. ✅ Red-proof: the capability
        /// ignored ⇒ it drives out to the firing point ⇒ red.
        /// </summary>
        [Fact]
        public void CE2197_AMobilityKill_StandsAndFights_AndNeverDrivesAgain()
        {
            var d = new Duel(Tank());
            d.ToHide(HideA);
            int moves = d.Moves.Count;
            Caps(d, ActorCapabilities.CanShoot | ActorCapabilities.CanAim);
            d.Step();
            Assert.Equal(VehiclePhase.StandAndFight, d.Ws.Phase);
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
            d.Answer(VehicleFightFromCoverNodes.HideSite, 8, HideB);
            d.Run(10);
            Assert.Equal(VehiclePhase.StandAndFight, d.Ws.Phase);
            Assert.Equal(moves, d.Moves.Count);
            Assert.True(d.AimedRounds > 0, "it fires from where it is");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2197</c> — a FIREPOWER kill (the turret or gun — <c>CanShoot</c> stripped) while engaging: it withdraws into its hide and
        /// never exposes again, however many firing points answer; the gun stops. Both kills ⇒ Failure (nothing it can do). ✅ Red-proof:
        /// the capability ignored ⇒ it exposes again ⇒ red.
        /// </summary>
        [Fact]
        public void CE2197_AFirepowerKill_WithdrawsAndNeverExposesAgain_BothKillsFail()
        {
            var d = new Duel(Tank());
            d.ToHide(HideA);
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 7, FiringA);
            Assert.Equal(VehiclePhase.Engaging, d.RunUntil(VehiclePhase.Engaging));
            Caps(d, ActorCapabilities.CanMove);
            d.Step();
            Assert.Equal(VehiclePhase.Withdrawing, d.Ws.Phase);
            Assert.Equal((HideA, true), d.Moves[^1]);   // reverse back into the hide
            Assert.Equal(VehiclePhase.Hidden, d.RunUntil(VehiclePhase.Hidden));
            int moves = d.Moves.Count;
            d.Answer(VehicleFightFromCoverNodes.FiringSite, 11, FiringA, FiringB);
            d.Run(20);
            Assert.Equal(VehiclePhase.Hidden, d.Ws.Phase);
            Assert.Equal(moves, d.Moves.Count);
            Assert.False(d.Firing);

            Caps(d, ActorCapabilities.None);
            Assert.Equal(NodeStatus.Failure, d.Step());
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2195</c> — the designation beside the cycle (both trees: <c>Parallel(1){ UntilTargetDown ‖ the cycle }</c>, a RequireOne
        /// Parallel that FAILS only when every child failed): with NO target named it must ABSTAIN (Failure), or a cycle that failed — no
        /// hide — leaves the behaviour Running for ever. 📌 Live <c>ua-tank-corner</c>: the tank sat 40 s in a dead reaction, never moved,
        /// and the SOP could not react again. With a target: Running while it lives (designated), Success once it is down.
        /// ✅ Red-proof: the old <c>Running</c> for no target ⇒ red.
        /// </summary>
        [Fact]
        public void CE2195_TheDesignation_AbstainsWithNoTarget_SoAFailedCycleEndsTheBehaviour()
        {
            var d = new Duel(Tank());
            var none = default(DesignateParams);
            var ws = default(DesignateState);
            Assert.Equal(NodeStatus.Failure, PostureNodes.UntilTargetDown(ref none, ref ws, d.Self, d.Repo));

            if (!d.Repo.HasComponent<Health>(d.Enemy)) d.Repo.AddComponent(d.Enemy, new Health { Current = 100f, Max = 100f });
            var named = new DesignateParams { TargetPacked = (long)d.Enemy.PackedValue };
            ws = default;
            Assert.Equal(NodeStatus.Running, PostureNodes.UntilTargetDown(ref named, ref ws, d.Self, d.Repo));
            Assert.True(DesignatedTarget.Of(d.Repo, d.Self, out var designated) && designated.Equals(d.Enemy));
            d.Repo.GetComponentRW<Health>(d.Enemy).Current = 0f;
            Assert.Equal(NodeStatus.Success, PostureNodes.UntilTargetDown(ref named, ref ws, d.Self, d.Repo));
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
