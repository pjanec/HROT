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
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.AI.Behaviors.Brains;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.CGF.Configuration;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;
using WeaponState = Fdp.Toolkit.Combat.Components.WeaponState;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3136</c> P-6 — <see cref="PeekAndFireNodes.PeekAndFire"/> (D7 + D8, B1–B8; 📄 docs/DESIGN_Peek_And_Fire.md §8): the
    /// stance peek and the step peek cycles, a window burned by heat and left for another, the blind burst, staying down while
    /// reloading or under fire, the position memory outliving an exit, and the T4 gizmo's text. The node runs on a world whose
    /// body, weapon and EQS are FAKED in the test (a move arrives at once, a weapon fires one round a step, the test writes the
    /// sensors' answers) — what is under test is the node's decisions, not the executors (P-3 / P-5 / B7 rail those).
    /// </summary>
    public sealed unsafe class PeekAndFireNodesTests
    {
        private static readonly Vector3 EnemyAt = new(20f, 0f, 0f);

        private sealed class Duel
        {
            public readonly EntityRepository Repo = new();
            public readonly Entity Self, Enemy;
            public PeekAndFireParams P;
            public PeekAndFireState Ws;
            public double Time;
            public readonly List<Vector3> Moves = new();
            public readonly List<(ushort Action, int Rounds, Vector3 Point)> Fires = new();
            public int AimedRounds, BlindRounds;
            private uint _moveInst, _fireInst;
            private int _fired;
            private long _frame;

            public Duel(PeekAndFireParams p, bool seen = true)
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                Fdp.Toolkit.Utility.StandardInputs.RegisterAll();   // the threat ranking's inputs, as CgfLogicPack's scan does
                if (!Repo.IsComponentTypeRegistered<ActiveSensorTracks>()) Repo.RegisterComponent<ActiveSensorTracks>();
                P = p;

                Self = Repo.CreateEntity();
                Repo.AddComponent(Self, new BehaviorState());
                Repo.AddComponent(Self, new LocomotionChannel());
                Repo.AddComponent(Self, new WeaponChannel());
                Repo.AddComponent(Self, new WeaponState { Ammo = 150, MaxAmmo = 150, MagazineSize = 30, MagazineRounds = 30, ReloadSeconds = 3f });
                Repo.AddComponent(Self, new StanceIntent());
                Repo.AddComponent(Self, new RecentSenses());
                Repo.AddComponent(Self, new ActiveSensorTracks());

                Enemy = Repo.CreateEntity();
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

            /// <summary>One node tick at the current time, then the faked body and weapon answer; then time moves on.</summary>
            public NodeStatus Step(double dt = 0.1)
            {
                Clock();
                var status = PeekAndFireNodes.PeekAndFire(ref P, ref Ws, Self, Repo);
                Repo.FlushCommandBuffers();   // a sensor's destroy is deferred, as in production
                Body();
                Weapon();
                Time += dt;
                return status;
            }

            public void Run(double seconds) { double end = Time + seconds; while (Time < end) Step(); }

            public PeekPhase RunUntil(PeekPhase phase, double maxSeconds = 30)
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
                    fixed (byte* src = loco.Params) Moves.Add(((MoveToParams*)src)->Destination);
                    return;   // arrives on the next step
                }
                loco.Status = NodeStatus.Success;
            }

            private void Weapon()
            {
                ref var ch = ref Repo.GetComponentRW<WeaponChannel>(Self);
                if (ch.ActiveAction == 0 || ch.Status != NodeStatus.Running) return;
                bool aimed = ch.ActiveAction == CombatConstants.ActionIdAimAndFire;
                int rounds = aimed ? Unsafe.As<byte, AimAndFireParams>(ref ch.Params[0]).Rounds
                                   : Unsafe.As<byte, FireAtPointParams>(ref ch.Params[0]).Rounds;
                if (ch.ActionInstanceId != _fireInst)
                {
                    _fireInst = ch.ActionInstanceId;
                    _fired = 0;
                    Fires.Add((ch.ActiveAction, rounds, aimed ? default : Unsafe.As<byte, FireAtPointParams>(ref ch.Params[0]).Point));
                }
                _fired++;
                if (aimed) AimedRounds++; else BlindRounds++;
                if (rounds > 0 && _fired >= rounds) ch.Status = NodeStatus.Success;
            }

            public StanceId Stance => Repo.GetComponentRO<StanceIntent>(Self).TargetStance;

            public Entity Sensor(int site) => EqsChildSensor.Find(Repo, Self, site);

            /// <summary>Writes a sensor's answer: points in rank order (score falls by 0.1 per rank), each with an optional stance.</summary>
            public void Answer(int site, uint tick, params (float X, float Y, StanceId? Stance)[] points)
            {
                var sensor = Sensor(site);
                Assert.False(sensor.IsNull, $"the node keeps its sensor 0x{site:X8}");
                var buf = new EqsCognitiveBuffer { Count = (byte)points.Length, LastUpdateTick = tick };
                var span = buf.GetSpanRW();
                for (int i = 0; i < points.Length; i++)
                    span[i] = new EqsResult
                    {
                        PositionX = points[i].X, PositionY = points[i].Y, Score = 1f - 0.1f * i,
                        Stance = points[i].Stance is { } s ? (byte)((byte)s + 1) : (byte)0,
                    };
                Repo.SetComponent(sensor, buf);
            }

            public FiringPositionMemory Memory => UnitMemory.Get<FiringPositionMemory>(Repo, Self);

            public void Sense(SensorChange kind) => Repo.GetComponentRW<RecentSenses>(Self).Record(kind, Time);
        }

        private static PeekAndFireParams Window(int exposuresPerPosition = 3) => new()
        {
            HideTemplate = FindWindowFiringPosition.BlueprintId, HideSecondsMin = 2f, HideSecondsMax = 2f,
            ExposuresPerPosition = exposuresPerPosition,
        };

        private static PeekAndFireParams Street() => new()
        {
            HideTemplate = FindCoverFromTarget.BlueprintId, HideSecondsMin = 2f, HideSecondsMax = 2f,
        };

        /// <summary>
        /// ⭐⭐ <c>P6_R1</c> — THE STANCE PEEK (A at a window): to the window, PRONE under the sill, up to the window's stance (crouched),
        /// three aimed rounds at the seen enemy, back down; the exposure counts one use and B2's heat on the window.
        /// </summary>
        [Fact]
        public void P6_R1_StancePeek_HidesProne_ExposesCrouched_FiresThreeAimed_HidesAgain()
        {
            var d = new Duel(Window());
            d.Step();
            Assert.False(d.Sensor(PeekAndFireNodes.CoverSite).IsNull, "the node points its own cover sensor at the threat");
            Assert.Equal(d.Enemy, d.Repo.GetComponentRO<EqsSensor>(d.Sensor(PeekAndFireNodes.CoverSite)).ContextSlot1);
            Assert.Equal(PeekPhase.Choose, d.Ws.Phase);

            d.Answer(PeekAndFireNodes.CoverSite, 5, (2f, 3f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(new Vector3(2f, 3f, 0f), Assert.Single(d.Moves));
            Assert.Equal(StanceId.Prone, d.Stance);
            Assert.True(d.Sensor(PeekAndFireNodes.PeekSite).IsNull, "a stance peek needs no step-out sensor");

            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            Assert.Equal(StanceId.Crouched, d.Stance);
            var fire = Assert.Single(d.Fires);
            Assert.Equal(CombatConstants.ActionIdAimAndFire, fire.Action);
            Assert.Equal(3, fire.Rounds);

            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(StanceId.Prone, d.Stance);
            Assert.Equal(3, d.AimedRounds);
            Assert.Equal(0, d.BlindRounds);
            var m = d.Memory;
            Assert.Equal(1, m.Count);
            Assert.Equal(1, m.Uses[0]);
            Assert.InRange(m.Heat[0], 0.99f, 1.01f);
            Assert.Single(d.Moves);   // a stance peek never leaves the spot
        }

        /// <summary>
        /// ⭐⭐ <c>P6_R2</c> — THE STEP PEEK (B at a corner): to the cover point at its stance, then out to the step-peek sensor's point,
        /// standing, aimed fire, and back to the cover point — the same machine, other parameters.
        /// </summary>
        [Fact]
        public void P6_R2_StepPeek_StepsOutToThePeekPoint_FiresAndStepsBack()
        {
            var d = new Duel(Street());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (10f, 10f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(StanceId.Crouched, d.Stance);
            Assert.Equal(1, d.Ws.PeekIsStep);

            d.Step();
            d.Answer(PeekAndFireNodes.PeekSite, 7, (10f, 12f, null));
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            Assert.Equal(StanceId.Standing, d.Stance);
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));

            Assert.Equal(new[] { new Vector3(10f, 10f, 0f), new Vector3(10f, 12f, 0f), new Vector3(10f, 10f, 0f) }, d.Moves);
            Assert.Equal(StanceId.Crouched, d.Stance);
            Assert.Equal(3, d.AimedRounds);
            var m = d.Memory;
            Assert.Equal(new Vector3(10f, 10f, 0f), new Vector3(m.X[0], m.Y[0], m.Z[0]));   // the POSITION heats, not the step
        }

        /// <summary>
        /// ⭐⭐⭐ <c>P6_R3</c> — THE DESIGN RAIL: a window is BURNED by heat after three quick exposures (B2–B4: 1 + 0.94 + 0.88 ≥ 2.5) and
        /// the unit moves to the next window — ⭐ the counter is set high, so it is the HEAT that moves it. ⭐ B1: after the node exits
        /// (state reset) a new run still skips the burned window — the memory outlived the exit. 🔴 Red-proof: with the node state
        /// as the store (D8's superseded storage) the re-entered run would pick the first window again.
        /// </summary>
        [Fact]
        public void P6_R3_AWindowBurnedAfterThreeUses_IsLeftForAnother_AndStaysBurnedAcrossAnExit()
        {
            var d = new Duel(Window(exposuresPerPosition: 10));
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (0f, 0f, StanceId.Crouched), (6f, 0f, StanceId.Crouched));

            for (int exposure = 0; exposure < 3; exposure++)
            {
                Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
                Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            }
            var m = d.Memory;
            Assert.Equal(3, m.Uses[0]);
            Assert.Equal(1, m.Burned[0]);

            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide));
            Assert.Equal(new Vector3(6f, 0f, 0f), d.Moves[^1]);

            PeekAndFireNodes.Deactivate_PeekAndFire(ref d.P, ref d.Ws, d.Self, d.Repo);
            d.Repo.FlushCommandBuffers();
            Assert.True(d.Sensor(PeekAndFireNodes.CoverSite).IsNull, "the run's sensor goes with it");
            d.Ws = default;
            int moves = d.Moves.Count;
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 9, (0f, 0f, StanceId.Crouched), (6f, 0f, StanceId.Crouched));
            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide));
            Assert.Equal(moves + 1, d.Moves.Count);
            Assert.Equal(new Vector3(6f, 0f, 0f), d.Moves[^1]);   // the new run skips the burned window it never saw

            // 🔴 red-proof, in place: the same exit with the MEMORY wiped picks the first window again — the skip above is the
            //    memory's doing, not the answer's order.
            PeekAndFireNodes.Deactivate_PeekAndFire(ref d.P, ref d.Ws, d.Self, d.Repo);
            d.Repo.FlushCommandBuffers();
            d.Ws = default;
            UnitMemory.Set(d.Repo, d.Self, new FiringPositionMemory());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 11, (0f, 0f, StanceId.Crouched), (6f, 0f, StanceId.Crouched));
            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide));
            Assert.Equal(Vector3.Zero, d.Moves[^1]);
        }

        /// <summary>⭐⭐ <c>P6_R4</c> — the exposure counter alone also moves the unit (D8): three exposures, a cool window, the next one.</summary>
        [Fact]
        public void P6_R4_ExposuresPerPosition_RelocatesToTheNextWindow()
        {
            var d = new Duel(Window(exposuresPerPosition: 2) with { BurnHeat = 99f, ReuseHeat = 98f });
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (0f, 0f, StanceId.Crouched), (2f, 0f, StanceId.Crouched), (7f, 0f, StanceId.Crouched));
            for (int exposure = 0; exposure < 2; exposure++)
            {
                Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
                Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            }
            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide));
            Assert.Equal(new Vector3(7f, 0f, 0f), d.Moves[^1]);   // ≥ MinRelocateMetres (4 m): the 2 m window is too close
        }

        /// <summary>
        /// ⭐⭐ <c>P6_R5</c> — NOT SEEN when exposed (4.2): after <c>GraceSeconds</c> a blind burst of <c>BlindRounds</c> at the
        /// REMEMBERED spot raised to the body's middle — and no aimed round at all.
        /// </summary>
        [Fact]
        public void P6_R5_NotSeenWithinTheGrace_FiresABlindBurstAtTheRememberedSpot()
        {
            var d = new Duel(Window(), seen: false);
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (0f, 0f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Expose, d.RunUntil(PeekPhase.Expose));
            double up = d.Time;
            Assert.Equal(PeekPhase.Blind, d.RunUntil(PeekPhase.Blind));
            Assert.InRange(d.Time - up, 0.55, 0.85);   // the grace (0.6 s) on 0.1 s steps

            var fire = Assert.Single(d.Fires);
            Assert.Equal(CombatConstants.ActionIdFireAtPoint, fire.Action);
            Assert.Equal(3, fire.Rounds);
            Assert.Equal(EnemyAt + new Vector3(0f, 0f, 1f), fire.Point);
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(0, d.AimedRounds);
            Assert.Equal(3, d.BlindRounds);
        }

        /// <summary>
        /// ⭐⭐ <c>P6_R6</c> — B8: RELOADING keeps the unit down past its wait, and so does being SHOT AT (a near miss within
        /// <c>SuppressedSeconds</c>); once both are over it comes up.
        /// </summary>
        [Fact]
        public void P6_R6_ReloadingOrSuppressed_StaysDown()
        {
            var d = new Duel(Window());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (0f, 0f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));

            d.Repo.GetComponentRW<WeaponState>(d.Self).ReloadSecondsRemaining = 99f;
            d.Run(5);
            Assert.Equal(PeekPhase.Hidden, d.Ws.Phase);
            Assert.Equal(StanceId.Prone, d.Stance);

            d.Repo.GetComponentRW<WeaponState>(d.Self).ReloadSecondsRemaining = 0f;
            d.Sense(SensorChange.NearMiss);
            d.Run(2.5);
            Assert.Equal(PeekPhase.Hidden, d.Ws.Phase);
            Assert.Equal(PeekPhase.Expose, d.RunUntil(PeekPhase.Expose, 1.0));   // the near miss is 3 s old: up again
        }

        /// <summary>
        /// ⭐⭐ <c>P6_R7</c> — near-missed WHILE EXPOSED: the unit goes down at once, and the window heats by <c>HeatWhenFiredUpon</c> on top
        /// of the exposure (B2: being shot at there is the strongest reason to leave).
        /// </summary>
        [Fact]
        public void P6_R7_FiredUponWhileExposed_GoesDown_AndHeatsMore()
        {
            var d = new Duel(Window());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (0f, 0f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            d.Sense(SensorChange.NearMiss);
            d.Step();
            Assert.Equal(PeekPhase.Hidden, d.Ws.Phase);
            Assert.True(d.AimedRounds < 3, "the burst was cut short");
            var m = d.Memory;
            Assert.InRange(m.Heat[0], 2.49f, 2.51f);
        }

        /// <summary>⭐ <c>P6_R8</c> — nothing remembered: Success, and the node's sensor goes.</summary>
        [Fact]
        public void P6_R8_NothingRemembered_Succeeds()
        {
            var d = new Duel(Window());
            d.Step();
            d.Repo.GetComponentRW<TargetMemory>(d.Self).Count = 0;
            Assert.Equal(NodeStatus.Success, d.Step());
            Assert.True(d.Sensor(PeekAndFireNodes.CoverSite).IsNull);
        }

        /// <summary>
        /// ⭐⭐ <c>P6_R9</c> — the heat rules (B2–B5) on their own: halving per half-life, hysteresis (burned at 2.5, still burned at
        /// 1.5, usable below 1.0), a candidate within the match radius IS the slot, a full memory replaces its coolest slot.
        /// </summary>
        [Fact]
        public void P6_R9_HeatCools_BurnsWithHysteresis_MatchesWithinARadius_ReplacesTheCoolest()
        {
            var p = PeekAndFireNodes.Rules(PeekAndFireNodes.Effective(default));
            var m = new FiringPositionMemory();
            int a = PositionHeat.Add(ref m, Vector3.Zero, 3f, 0d, in p, exposure: true);
            Assert.True(PositionHeat.IsBurned(ref m, a, 0d, in p));
            Assert.InRange(PositionHeat.HeatNow(ref m, a, 45d, p.CoolHalfLifeSeconds), 1.49f, 1.51f);
            Assert.True(PositionHeat.IsBurned(ref m, a, 45d, in p), "between ReuseHeat and BurnHeat it stays burned");
            Assert.False(PositionHeat.IsBurned(ref m, a, 72d, in p), "below ReuseHeat (1.0) it is usable again");
            Assert.InRange(PositionHeat.Penalty(ref m, Vector3.Zero, 72d, in p), 0.28f, 0.30f);   // warm: heat × 0.3

            Assert.Equal(a, PositionHeat.Add(ref m, new Vector3(0.8f, 0f, 0f), 0f, 72d, in p, exposure: false));   // within 1 m
            for (int i = 1; i < FiringPositionMemory.Capacity; i++) PositionHeat.Add(ref m, new Vector3(10f * i, 0f, 0f), 2f, 72d, in p, false);
            Assert.Equal(FiringPositionMemory.Capacity, (int)m.Count);
            int replaced = PositionHeat.Add(ref m, new Vector3(-50f, 0f, 0f), 1f, 72d, in p, exposure: true);
            Assert.Equal(a, replaced);   // slot 0 was the coolest
            Assert.Equal(-50f, m.X[a]);
        }

        /// <summary>
        /// ⭐ <c>P6_R10</c> — T4: the gizmo's text reads what the node mirrored into the unit memory — the phase and its timer while
        /// the node runs, nothing once it has stopped; a slot's heat and uses.
        /// </summary>
        [Fact]
        public void P6_R10_TheGizmo_SaysThePhase_WhileTheNodeRuns()
        {
            var d = new Duel(Window());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (0f, 0f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            var m = UnitMemory.GetInView<FiringPositionMemory>(d.Repo, d.Self);
            Assert.Equal((byte)PeekPhase.Hidden, m.Phase);
            Assert.StartsWith("PF hidden 1.", PeekAndFireGizmo.PhaseText(in m, d.Time));
            Assert.Null(PeekAndFireGizmo.PhaseText(in m, d.Time + 5d));
            Assert.Equal("h1.7 x3", PeekAndFireGizmo.SlotText(1.66f, 3));
        }
    }
}
