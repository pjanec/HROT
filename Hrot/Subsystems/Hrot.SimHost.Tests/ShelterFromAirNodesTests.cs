using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Xunit;
using Locomotion = Fdp.Toolkit.Motion.Locomotion;
using LocomotionClass = Fdp.Toolkit.Tkb.Domain.LocomotionClass;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2165</c> V8 — <see cref="ShelterFromAirNodes.ShelterFromAir"/> (📄 docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md
    /// §8): under overhead cover when the shelter query answers, else keep moving away from the drone, leg after leg; fire at it while it is
    /// seen; done once the sky has been clear for <c>ClearSeconds</c>. Body, weapon and EQS faked as in <see cref="VehicleFightFromCoverNodesTests"/>.
    /// </summary>
    public sealed unsafe class ShelterFromAirNodesTests
    {
        private static readonly Vector3 DroneAt = new(0f, 60f, 30f), ShelterAt = new(-20f, -10f, 0f);

        private sealed class Raid
        {
            public readonly EntityRepository Repo = new();
            public readonly Entity Self, Drone;
            public ShelterFromAirParams P;
            public ShelterFromAirState Ws;
            public double Time;
            public readonly List<(Vector3 To, bool Reverse)> Moves = new();
            public readonly List<Entity> AimedAt = new();
            /// <summary>The fake weapon fails every aimed action (a mount that cannot elevate: out of arc).</summary>
            public bool OutOfArc;
            /// <summary>The fake body arrives (true) or never arrives (false: the leg keeps running).</summary>
            public bool Arrive = true;
            private uint _moveInst, _fireInst, _tick = 10;
            private long _frame;

            public Raid(ShelterFromAirParams p, bool seen = false)
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                if (!Repo.IsComponentTypeRegistered<ActiveSensorTracks>()) Repo.RegisterComponent<ActiveSensorTracks>();
                if (!Repo.IsComponentTypeRegistered<Locomotion>()) Repo.RegisterComponent<Locomotion>();
                P = p;
                Self = Repo.CreateEntity();
                Repo.AddComponent(Self, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
                Repo.AddComponent(Self, new BehaviorState());
                Repo.AddComponent(Self, new LocomotionChannel());
                Repo.AddComponent(Self, new WeaponChannel());
                Repo.AddComponent(Self, new ActiveSensorTracks());

                Drone = Repo.CreateEntity();
                Repo.AddComponent(Drone, new SimTransform { Position = DroneAt, Rotation = Quaternion.Identity });
                Repo.AddComponent(Drone, new Locomotion { Class = LocomotionClass.Rotorcraft });
                Repo.AddComponent(Self, new TargetMemory());
                ref var mem = ref Repo.GetComponentRW<TargetMemory>(Self);
                mem.EntityIds[0] = (long)Drone.PackedValue;
                mem.Freshness[0] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
                mem.Modalities[0] = (byte)SensorModality.Visual;
                mem.PositionsX[0] = DroneAt.X; mem.PositionsY[0] = DroneAt.Y; mem.PositionsZ[0] = DroneAt.Z;
                mem.LastSeenTick[0] = _tick;
                mem.Count = 1;
                See(seen);
                Clock();
            }

            public void See(bool seen)
            {
                ref var t = ref Repo.GetComponentRW<ActiveSensorTracks>(Self);
                t.Count = 0;
                if (!seen) return;
                t.EntityIds[0] = (long)Drone.PackedValue;
                t.Modalities[0] = (byte)SensorModality.Visual;
                t.Count = 1;
            }

            /// <summary>The drone is sensed again this frame somewhere else (a diving drone: its remembered spot moves).</summary>
            public void SenseAt(Vector3 at)
            {
                ref var mem = ref Repo.GetComponentRW<TargetMemory>(Self);
                mem.PositionsX[0] = at.X; mem.PositionsY[0] = at.Y; mem.PositionsZ[0] = at.Z;
                mem.LastSeenTick[0] = ++_tick;
            }

            /// <summary>The drone is sensed again this frame (a newer sighting / hearing tick).</summary>
            public void SenseAgain() => Repo.GetComponentRW<TargetMemory>(Self).LastSeenTick[0] = ++_tick;

            private void Clock()
            {
                Repo.SetSingleton(new GlobalTime { TotalTime = Time, DeltaTime = 0.1f, TimeScale = 1f, FrameNumber = ++_frame });
                Repo.SetSimulationTime((float)Time);
            }

            public NodeStatus Step(double dt = 0.1)
            {
                Clock();
                var status = ShelterFromAirNodes.ShelterFromAir(ref P, ref Ws, Self, Repo);
                Repo.FlushCommandBuffers();
                Body();
                Weapon();
                Time += dt;
                return status;
            }

            public NodeStatus Run(double seconds, bool sensed = false)
            {
                var status = NodeStatus.Running;
                double end = Time + seconds;
                while (Time < end && status == NodeStatus.Running) { if (sensed) SenseAgain(); status = Step(); }
                return status;
            }

            private void Body()
            {
                ref var loco = ref Repo.GetComponentRW<LocomotionChannel>(Self);
                if (loco.ActiveAction != NavigationConstants.ActionIdMoveTo || loco.Status != NodeStatus.Running) return;
                if (loco.ActionInstanceId != _moveInst)
                {
                    _moveInst = loco.ActionInstanceId;
                    fixed (byte* src = loco.Params) Moves.Add((((MoveToParams*)src)->Destination, ((MoveToParams*)src)->ReverseAllowed == 1));
                    return;
                }
                if (Arrive) loco.Status = NodeStatus.Success;
            }

            private void Weapon()
            {
                ref var ch = ref Repo.GetComponentRW<WeaponChannel>(Self);
                if (ch.ActiveAction != CombatConstants.ActionIdAimAndFire || ch.Status != NodeStatus.Running) return;
                if (ch.ActionInstanceId == _fireInst) return;
                _fireInst = ch.ActionInstanceId;
                AimedAt.Add(Unsafe.As<byte, AimAndFireParams>(ref ch.Params[0]).Target);
                if (OutOfArc) ch.Status = NodeStatus.Failure;
            }

            public Entity Sensor => EqsChildSensor.Find(Repo, Self, ShelterFromAirNodes.ShelterSite);

            public void AnswerShelter(params Vector3[] points)
            {
                var sensor = Sensor;
                Assert.False(sensor.IsNull, "the node keeps its shelter sensor");
                var buf = new EqsCognitiveBuffer { Count = (byte)points.Length, LastUpdateTick = 5 };
                var span = buf.GetSpanRW();
                for (int i = 0; i < points.Length; i++)
                    span[i] = new EqsResult { PositionX = points[i].X, PositionY = points[i].Y, PositionZ = points[i].Z, Score = 1f - 0.1f * i };
                Repo.SetComponent(sensor, buf);
            }
        }

        /// <summary>
        /// ⭐⭐⭐ <c>V8_R1</c> — UNDER THE DECK: the node asks <see cref="FindShelter"/> (aimed at the drone), drives to its best answer with
        /// reverse allowed, holds there while the drone is still sensed, and ends (Success, its sensor gone) once the sky has been clear for
        /// <c>ClearSeconds</c>. 🔴 Red-proof: hold the clock on <c>StartedAt</c> instead of the last sensing and it ends while the drone is
        /// still overhead.
        /// </summary>
        [Fact]
        public void V8_ShelterFromAir_GoesUnderTheDeck_HoldsWhileSensed_EndsWhenClear()
        {
            var r = new Raid(new ShelterFromAirParams { ClearSeconds = 4f });
            r.Step();
            Assert.Equal(FindShelter.BlueprintId, r.Repo.GetComponentRO<EqsSensor>(r.Sensor).BlueprintId);
            Assert.Equal(DroneAt, r.Repo.GetComponentRO<EqsSensor>(r.Sensor).ContextPoint1);
            r.AnswerShelter(ShelterAt, new Vector3(30f, 0f, 0f));
            r.Step(); r.Step();
            Assert.Equal((ShelterAt, true), Assert.Single(r.Moves));
            Assert.Equal(NodeStatus.Running, r.Run(1.0, sensed: true));
            Assert.Equal(ShelterPhase.Holding, r.Ws.Phase);

            Assert.Equal(NodeStatus.Running, r.Run(6.0, sensed: true));   // overhead for longer than ClearSeconds: still holding
            Assert.Equal(ShelterPhase.Holding, r.Ws.Phase);
            double clearFrom = r.Time;
            Assert.Equal(NodeStatus.Success, r.Run(10.0));
            Assert.InRange(r.Time - clearFrom, 3.8, 4.4);
            r.Repo.FlushCommandBuffers();
            Assert.True(r.Sensor.IsNull, "the sensor goes with the run");
            Assert.Single(r.Moves);
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-2212</c> — A DRONE THAT MOVES DOES NOT RESET THE SHELTER QUERY: its remembered spot moves 3 m every frame (a 30 m/s
        /// FPV drone), yet the sensor keeps its epoch, an answer that arrives is not wiped, and the vehicle drives under the deck.
        /// 🔴 Red-proof: re-point past 2 m (the PeekAndFire rule) and every frame clears the answer — the vehicle evades instead.
        /// </summary>
        [Fact]
        public void CE2212_AMovingDrone_DoesNotResetTheShelterQuery_TheVehicleStillShelters()
        {
            var r = new Raid(new ShelterFromAirParams { FindSeconds = 3f, ClearSeconds = 30f });
            r.Step();
            uint epoch = r.Repo.GetComponentRO<EqsSensor>(r.Sensor).Epoch;
            var at = DroneAt;
            for (int i = 0; i < 10; i++) { at += new Vector3(0f, -3f, 0f); r.SenseAt(at); r.Step(); }
            Assert.Equal(epoch, r.Repo.GetComponentRO<EqsSensor>(r.Sensor).Epoch);

            r.AnswerShelter(ShelterAt);
            for (int i = 0; i < 5; i++) { at += new Vector3(0f, -3f, 0f); r.SenseAt(at); r.Step(); }
            Assert.Equal((ShelterAt, true), Assert.Single(r.Moves));
            Assert.NotEqual(ShelterPhase.Evading, r.Ws.Phase);
        }

        /// <summary>
        /// ⭐⭐⭐ <c>V8_R2</c> — NO SHELTER: after <c>FindSeconds</c> without an answer the vehicle EVADES — each leg ends farther from the
        /// drone than it starts, legs swerve to alternate sides, and a new leg follows every arrival (it never stops). 🔴 Red-proof: drop the
        /// next-leg branch and it stops after one leg.
        /// </summary>
        [Fact]
        public void V8_NoShelter_KeepsMoving_AwayFromTheDrone()
        {
            var r = new Raid(new ShelterFromAirParams { FindSeconds = 1f, ClearSeconds = 30f });
            Assert.Equal(NodeStatus.Running, r.Run(0.9, sensed: true));
            Assert.Empty(r.Moves);
            Assert.Equal(NodeStatus.Running, r.Run(2.0, sensed: true));
            Assert.Equal(ShelterPhase.Evading, r.Ws.Phase);
            Assert.True(r.Moves.Count >= 3, $"leg after leg ({r.Moves.Count})");
            foreach (var (to, reverse) in r.Moves)
            {
                Assert.True(Vector2.Distance(new Vector2(to.X, to.Y), new Vector2(DroneAt.X, DroneAt.Y)) > Vector2.Distance(Vector2.Zero, new Vector2(DroneAt.X, DroneAt.Y)),
                    $"{to} is away from the drone");
                Assert.False(reverse);
            }
            Assert.True(r.Moves[0].To.X * r.Moves[1].To.X < 0f, "the legs swerve to alternate sides");
        }

        /// <summary>⭐ <c>V8_R2b</c> — a shelter that answers WHILE evading is taken.</summary>
        [Fact]
        public void V8_AShelterFoundWhileEvading_IsTaken()
        {
            var r = new Raid(new ShelterFromAirParams { FindSeconds = 0.5f, ClearSeconds = 30f }) { Arrive = false };
            r.Run(1.0, sensed: true);
            Assert.Equal(ShelterPhase.Evading, r.Ws.Phase);
            r.AnswerShelter(ShelterAt);
            r.Run(0.3, sensed: true);
            Assert.Equal(ShelterPhase.ToShelter, r.Ws.Phase);
            Assert.Equal((ShelterAt, true), r.Moves[^1]);
        }

        /// <summary>
        /// ⭐⭐ <c>V8_AnHmg_FiresAtASeenDrone_ATankGun_IsOutOfArc</c> (the behaviour half) — a SEEN drone is fired at; not seen, no fire; when
        /// the weapon brain fails the action (a tank gun cannot elevate: out of arc) the node waits <see cref="ShelterFromAirNodes.RetryFireSeconds"/>
        /// before trying again — not every frame. 🔴 Red-proof: drop the retry gate and the failed fire is re-issued each step.
        /// </summary>
        [Fact]
        public void V8_ASeenDrone_IsFiredAt_AndAnOutOfArcFailure_IsRetriedLater()
        {
            var r = new Raid(new ShelterFromAirParams { ClearSeconds = 30f }, seen: true);
            r.Step(); r.Step();
            Assert.Equal(r.Drone, Assert.Single(r.AimedAt));

            var blind = new Raid(new ShelterFromAirParams { ClearSeconds = 30f }, seen: false);
            blind.Run(1.0, sensed: true);
            Assert.Empty(blind.AimedAt);

            var tank = new Raid(new ShelterFromAirParams { ClearSeconds = 30f }, seen: true) { OutOfArc = true };
            tank.Run(1.0, sensed: true);
            Assert.Single(tank.AimedAt);                     // one try in the first second, not ten
            tank.Run(2.0, sensed: true);
            Assert.Equal(2, tank.AimedAt.Count);             // tried again after the wait
        }

        /// <summary>⭐ <c>V8_R5</c> — leaving: the sensor goes, the fire and the move stop.</summary>
        [Fact]
        public void V8_Leaving_ReleasesTheSensorFireAndMove()
        {
            var r = new Raid(new ShelterFromAirParams { ClearSeconds = 30f }, seen: true) { Arrive = false };
            r.Step();
            r.AnswerShelter(ShelterAt);
            r.Step(); r.Step();
            ShelterFromAirNodes.Deactivate_ShelterFromAir(ref r.P, ref r.Ws, r.Self, r.Repo);
            r.Repo.FlushCommandBuffers();
            Assert.True(r.Sensor.IsNull);
            Assert.NotEqual(CombatConstants.ActionIdAimAndFire, r.Repo.GetComponentRO<WeaponChannel>(r.Self).ActiveAction);
            Assert.NotEqual(NodeStatus.Running, LocomotionMoveTo.Status(r.Repo, r.Self));
        }
    }
}
