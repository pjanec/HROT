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
            /// <summary>⭐ <c>CE-3158</c> G1 — the kind the cover sensor's points carry, as the real generator writes it: a window
            /// template's <see cref="CoverPointsGenerator"/> answers windows, the cover template's answers covers.</summary>
            public CoverKind Kind;
            public double Time;
            public readonly List<Vector3> Moves = new();
            public readonly List<(ushort Action, int Rounds, Vector3 Point)> Fires = new();
            public int AimedRounds, BlindRounds;
            /// <summary>⭐ <c>CE-3158</c> G2 — moves to these points FAIL (an unreachable point) instead of arriving.</summary>
            public readonly List<Vector3> Unreachable = new();
            /// <summary>⭐ <c>CE-3158</c> G4 — the target of every aimed action started, in order.</summary>
            public readonly List<Entity> AimedAt = new();
            /// <summary>⭐ <c>CE-2228</c> — a command-guided round is in flight: the aimed fire stays Running and says <c>Guiding</c>.</summary>
            public bool Guiding;
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
                Kind = p.HideTemplate == FindWindowFiringPosition.BlueprintId ? CoverKind.WindowFiring : CoverKind.Cover;

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
                loco.Status = Unreachable.Contains(Moves[^1]) ? NodeStatus.Failure : NodeStatus.Success;
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
                    if (aimed) AimedAt.Add(Unsafe.As<byte, AimAndFireParams>(ref ch.Params[0]).Target);
                }
                if (aimed && Guiding)
                {
                    ref var row = ref Repo.GetComponentRW<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(Self);
                    row.Weapon.Reason = Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Guiding;
                    row.Weapon.At = Time;
                    return;
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
                        // the cover sensor answers cover points of the duel's kind; the step-peek sensor's open points are not cover
                        Kind = site == PeekAndFireNodes.CoverSite ? EqsResult.EncodeKind(Kind) : (byte)0,
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

        /// <summary>B's column: a step peek from cover, as <c>WindowDuelBehavior</c> flies it (⭐ <c>CE-3158</c> G1: <c>Auto</c> on a
        /// crouched cover now comes up on the spot — the duel's B asks for the step explicitly).</summary>
        private static PeekAndFireParams Street() => new()
        {
            HideTemplate = FindCoverFromTarget.BlueprintId, Mode = PeekMode.Step, HideSecondsMin = 2f, HideSecondsMax = 2f,
        };

        /// <summary>A generic rifleman: cover template, the peek chosen from the point (<see cref="PeekMode.Auto"/>).</summary>
        private static PeekAndFireParams Rifleman() => new()
        {
            HideTemplate = FindCoverFromTarget.BlueprintId, HideSecondsMin = 2f, HideSecondsMax = 2f,
        };

        // ── ⭐ CE-2275 D-2l — the frame lean (DA-77…DA-84, R-2059; 📄 DESIGN_Defend_And_Attack_Position.md §8c D-2l) ─────────────────

        /// <summary>A window in a wall along x = 1 (outward +X, toward the enemy at (20, 0)), its opening at (1, 2): the frame points 0.5 m
        /// inside — A on the left of a man looking out (+Y), B on his right. The enemy lies to the −Y side of the opening.</summary>
        private static readonly Vector3 FrameA = new(0.5f, 2.75f, 0f), FrameB = new(0.5f, 1.25f, 0f), Opening = new(1f, 2f, 0f);

        private static PeekAndFireParams Framed(int exposuresPerPosition = 3) => Window(exposuresPerPosition) with
        {
            Frame = 1, FrameA = FrameA, FrameB = FrameB, Opening = Opening, Anchor = FrameA, AnchorRadius = 4f,
        };

        private static Fdp.Toolkit.Combat.LeanSide LeanOf(Duel d) => Fdp.Toolkit.Combat.Leans.Of(d.Repo, d.Self);

        /// <summary>
        /// ⭐⭐ <c>D2l_R1</c> — AT A FRAME POST (DA-79, DA-80, DA-83): he goes to the frame point AWAY from the enemy (A — the enemy is on B's
        /// side), hides there STANDING and straight, exposes by LEANING toward the opening (his right, from A looking out), fires only once
        /// the lean has come out, and straightens again — never prone, never off the spot, no cover answer needed. ✅ Red-proofs: Choose
        /// skipping the frame ⇒ no move (waits for the cover sensor); the lean delay removed ⇒ a round in the lean's first 0.4 s; the
        /// straighten dropped ⇒ still leaning when hidden.
        /// </summary>
        [Fact]
        public void D2l_R1_AtAFramePost_HidesStandingBesideTheFrame_LeansOutToFire_AndStraightens()
        {
            var d = new Duel(Framed());
            d.Step();
            Assert.Equal(FrameA, Assert.Single(d.Moves));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(StanceId.Standing, d.Stance);
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanNone, LeanOf(d));

            Assert.Equal(PeekPhase.Expose, d.RunUntil(PeekPhase.Expose));
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanRight, LeanOf(d));
            double leanAt = d.Ws.LeanAt;
            while (d.Time < leanAt + PeekAndFireNodes.LeanSeconds - 0.05) { d.Step(); Assert.Empty(d.Fires); }
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed, 1.0));
            Assert.Equal(StanceId.Standing, d.Stance);
            Assert.Equal(1, d.Ws.Exposures);

            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanNone, LeanOf(d));
            Assert.Equal(3, d.AimedRounds);
            Assert.Single(d.Moves);
        }

        /// <summary>
        /// ⭐⭐ <c>D2l_R2</c> — a MASKED shot (the frame stops it) burns this side: he crosses to the OTHER frame point; and an exposure count
        /// spent does the same (DA-79's relocate). ✅ Red-proof: the crossing dropped ⇒ he stays at A (the sill fallback never runs: the cover
        /// sensor has no answer).
        /// </summary>
        [Fact]
        public void D2l_R2_AMaskedShot_OrUsedUpExposures_CrossesToTheOtherFramePoint()
        {
            var d = new Duel(Framed());
            d.Repo.AddComponent(d.Self, new Fdp.Toolkit.Behavior.Diagnostics.ActionStatus());
            d.Step();
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            ref var status = ref d.Repo.GetComponentRW<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(d.Self);
            status.Weapon.Reason = Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Masked;
            status.Weapon.At = d.Time;
            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide, 5));
            Assert.Equal(FrameB, d.Moves[^1]);
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanNone, LeanOf(d));

            var e = new Duel(Framed(exposuresPerPosition: 2) with { BurnHeat = 99f, ReuseHeat = 98f });
            e.Step();
            for (int exposure = 0; exposure < 2; exposure++)
            {
                Assert.Equal(PeekPhase.Aimed, e.RunUntil(PeekPhase.Aimed));
                Assert.Equal(PeekPhase.Hidden, e.RunUntil(PeekPhase.Hidden));
            }
            Assert.Equal(PeekPhase.MoveToHide, e.RunUntil(PeekPhase.MoveToHide));
            Assert.Equal(FrameB, e.Moves[^1]);
            Assert.Equal(PeekPhase.Expose, e.RunUntil(PeekPhase.Expose));
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanLeft, LeanOf(e));   // from B, looking out, the opening is on his left
        }

        /// <summary>
        /// ⭐⭐ <c>D2l_R3</c> — the frame point cannot be reached (DA-84): he fights the post the SILL way from then on — the cover sensor's
        /// window point, prone under it. ✅ Red-proof: the failure not latched ⇒ he walks to the frame again.
        /// </summary>
        [Fact]
        public void D2l_R3_AnUnreachableFrame_FallsBackToTheSill()
        {
            var d = new Duel(Framed());
            d.Unreachable.Add(FrameA);
            d.Step();
            d.Step(); d.Step();
            Assert.Equal((byte)1, d.Ws.FrameOff);
            d.Answer(PeekAndFireNodes.CoverSite, 5, (1f, 0.5f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(new Vector3(1f, 0.5f, 0f), d.Moves[^1]);
            Assert.Equal(StanceId.Prone, d.Stance);
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanNone, LeanOf(d));
        }

        /// <summary>
        /// ⭐⭐ <c>D2l_R4</c> — HOLD FIRE at a frame post: he leans out to WATCH and never fires (DA-6's observation); under fire he goes prone
        /// and straight. ✅ Red-proof: the HoldFire branch returning before the lean ⇒ he never leans.
        /// </summary>
        [Fact]
        public void D2l_R4_HoldFire_LeansOutToWatch_NeverFires_ProneUnderFire()
        {
            var d = new Duel(Framed());
            SetRoe(d, RoeFire.HoldFire);
            d.Step();
            d.Run(6);
            Assert.Equal(PeekPhase.Hidden, d.Ws.Phase);
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanRight, LeanOf(d));
            Assert.Equal(StanceId.Standing, d.Stance);
            Assert.Empty(d.Fires);
            d.Sense(SensorChange.NearMiss);
            d.Step();
            Assert.Equal(StanceId.Prone, d.Stance);
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanNone, LeanOf(d));
        }

        /// <summary>
        /// ⭐⭐ <c>D2l_R5</c> — <c>HoldWatch</c> (DA-80): with nothing to fight a man at a frame post walks to his frame point and leans out
        /// toward the opening by his body's facing — the same lean he fires from; the node's exit straightens him; a man with no frame post
        /// holds still and never leans. ✅ Red-proofs: no move ⇒ he leans from where he stands; the deactivator dropped ⇒ still leaning.
        /// </summary>
        [Fact]
        public void D2l_R5_HoldWatch_WalksToTheFramePoint_LeansToWatch_StraightensOnExit()
        {
            var d = new Duel(Framed(), seen: false);
            d.Repo.AddComponent(d.Self, new SimTransform { Position = new Vector3(-1f, 2.75f, 0f), Rotation = Quaternion.Identity });   // facing +X (east, out)
            var p = d.P;
            var ws = default(FrameWatchState);
            Assert.Equal(NodeStatus.Running, PostureNodes.HoldWatch(ref p, ref ws, d.Self, d.Repo));
            Assert.Equal((byte)1, ws.Moving);
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanNone, LeanOf(d));
            d.Repo.GetComponentRW<SimTransform>(d.Self).Position = FrameA;
            d.Repo.GetComponentRW<LocomotionChannel>(d.Self).Status = NodeStatus.Success;
            PostureNodes.HoldWatch(ref p, ref ws, d.Self, d.Repo);
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanRight, LeanOf(d));   // facing east at A, the opening (south-east) is on his right
            Assert.Equal(StanceId.Standing, d.Stance);
            PostureNodes.Deactivate_HoldWatch(ref p, ref ws, d.Self, d.Repo);
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanNone, LeanOf(d));

            var plain = Window();
            var ws2 = default(FrameWatchState);
            PostureNodes.HoldWatch(ref plain, ref ws2, d.Self, d.Repo);
            Assert.Equal((byte)0, ws2.Moving);
            Assert.Equal(Fdp.Toolkit.Combat.LeanSide.LeanNone, LeanOf(d));
        }

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
            Assert.Equal(EnemyAt, d.Repo.GetComponentRO<EqsSensor>(d.Sensor(PeekAndFireNodes.CoverSite)).ContextPoint1);   // D14: the remembered spot
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
        /// ⭐ <c>CE-2218</c> — at a window whose launcher line runs into the wall, the weapon holds <c>Masked</c>: the exposure ends at once, the
        /// window is BURNED (it cannot shoot that target), and the unit moves to the next window. A <c>Masked</c> left by an EARLIER exposure
        /// does not count. 🔴 Red-proof: without the node's masked check it stays up at the first window for the whole exposure.
        /// </summary>
        [Fact]
        public void CE2218_AMaskedShot_BurnsTheWindow_AndMovesToTheNext()
        {
            var d = new Duel(Window());
            d.Repo.AddComponent(d.Self, new Fdp.Toolkit.Behavior.Diagnostics.ActionStatus());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (2f, 3f, StanceId.Crouched), (8f, 3f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            Assert.False(PeekAndFireNodes.MaskedSince(d.Repo, d.Self, d.Time + 1.0), "a reason stamped before the exposure does not count");

            ref var status = ref d.Repo.GetComponentRW<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(d.Self);
            status.Weapon.Reason = Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Masked;
            status.Weapon.At = d.Time;
            d.Step();
            Assert.NotEqual(PeekPhase.Aimed, d.Ws.Phase);
            var m = d.Memory;
            Assert.True(m.Heat[0] >= d.P.BurnHeat, $"the window is burned (heat {m.Heat[0]:0.##})");

            status = ref d.Repo.GetComponentRW<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(d.Self);
            status.Weapon.Reason = Fdp.Toolkit.Behavior.Diagnostics.ActionReason.None;
            d.Run(10);
            Assert.Contains(new Vector3(8f, 3f, 0f), d.Moves);
        }

        /// <summary>
        /// ⭐ <c>CE-2228</c> (Exposure Cycle §7, V6: <i>"a TOW-type gunner holds sight through the flight"</i>) — with a command-guided round
        /// in flight the gunner stays up at the window past the exposure cap AND with the target out of sight, and comes down once it has
        /// landed. 🔴 Red-proof: without the guiding check he ducks at the exposure cap (or the lost-sight grace) and drops the missile.
        /// </summary>
        [Fact]
        public void CE2228_AGuidedRoundInFlight_HoldsTheGunnerUp_UntilItLands()
        {
            var d = new Duel(Window());
            d.Repo.AddComponent(d.Self, new Fdp.Toolkit.Behavior.Diagnostics.ActionStatus());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (2f, 3f, StanceId.Crouched), (8f, 3f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            d.Guiding = true;
            d.Step();
            Assert.True(PeekAndFireNodes.Guiding(d.Repo, d.Self));
            d.See(false);                                                       // the target drops out of sight mid-flight
            d.Run(PeekAndFireNodes.ExposeFor(in d.P, d.Repo, d.Self) + d.P.GraceSeconds + 2.0);
            Assert.Equal(PeekPhase.Aimed, d.Ws.Phase);                          // still up, still guiding

            d.Guiding = false;                                                  // the round lands: the aimed fire ends
            ref var ch = ref d.Repo.GetComponentRW<WeaponChannel>(d.Self);
            ch.Status = NodeStatus.Success;
            ref var row = ref d.Repo.GetComponentRW<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(d.Self);
            row.Weapon.Reason = Fdp.Toolkit.Behavior.Diagnostics.ActionReason.None;
            d.Step();
            Assert.NotEqual(PeekPhase.Aimed, d.Ws.Phase);                       // and down he comes
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
        /// ⭐⭐ <c>P7_R1</c> — D13, THE FRESHEST EVIDENCE WINS: the enemy was SEEN at tick 10 at its remembered spot, then a shot was
        /// HEARD at tick 50 far from it (an anonymous slot, as <c>TargetMemory.HearContact</c> makes for a sound &gt; 6 m away). The
        /// blind burst goes to the HEARD spot. And the other way round: a sighting newer than the sound keeps the burst on the enemy.
        /// 🔴 Red-proof: aim the burst with <c>EqsTacticsNodes.ThreatPosition</c> (P-6) and the first half shoots where the enemy WAS.
        /// </summary>
        [Fact]
        public void P7_R1_ABlindBurst_GoesToTheFreshestEvidence()
        {
            var heard = new Vector3(26f, 6f, 0f);
            Vector3 BurstWith(uint seenTick, uint heardTick)
            {
                var d = new Duel(Window(), seen: false);
                ref var mem = ref d.Repo.GetComponentRW<TargetMemory>(d.Self);
                mem.LastSeenTick[0] = seenTick;
                mem.EntityIds[1] = -7;
                mem.Anonymous[1] = 1;
                mem.Freshness[1] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
                mem.Modalities[1] = (byte)SensorModality.Acoustic;
                mem.PositionsX[1] = heard.X; mem.PositionsY[1] = heard.Y; mem.PositionsZ[1] = heard.Z;
                mem.LastSeenTick[1] = heardTick;
                mem.Count = 2;
                d.Step();
                d.Answer(PeekAndFireNodes.CoverSite, 5, (0f, 0f, StanceId.Crouched));
                Assert.Equal(PeekPhase.Blind, d.RunUntil(PeekPhase.Blind));
                return Assert.Single(d.Fires).Point;
            }

            Assert.Equal(heard + new Vector3(0f, 0f, 1f), BurstWith(seenTick: 10, heardTick: 50));      // heard after seen: the sound
            Assert.Equal(EnemyAt + new Vector3(0f, 0f, 1f), BurstWith(seenTick: 90, heardTick: 50));    // seen after heard: the enemy
        }

        /// <summary>
        /// ⭐⭐ <c>P8_R1</c> — <c>CE-3144</c> D14: the sensors look for where the enemy is REMEMBERED, never where he is — the enemy stands
        /// 30 m from his remembered spot and the sensor still points at the spot (a context POINT, no entity); a sighting that moves
        /// the spot more than 2 m re-points it; a newer heard contact takes it over. 🔴 Red-proof: pass <c>threat</c> instead of
        /// <c>SensorAim</c> in <c>KeepSensors</c> and the sensor names the enemy entity — whose true position, hidden, no window sees
        /// (the live P-8 run stood still for 600 s).
        /// </summary>
        [Fact]
        public void P8_R1_TheSensorsLookForTheRememberedSpot_NotTheEnemyItself()
        {
            var d = new Duel(Window());
            d.Repo.AddComponent(d.Enemy, new SimTransform { Position = new Vector3(20f, 30f, 0f), Rotation = Quaternion.Identity });
            d.Step();
            EqsSensor Cover() => d.Repo.GetComponentRO<EqsSensor>(d.Sensor(PeekAndFireNodes.CoverSite));
            Assert.True(Cover().ContextSlot1.IsNull, "no entity: the true position is not the sensor's business");
            Assert.Equal(EnemyAt, Cover().ContextPoint1);
            Assert.NotEqual(0, Cover().ContextPointMask & EqsSensor.Point1Bit);

            ref var mem = ref d.Repo.GetComponentRW<TargetMemory>(d.Self);
            mem.PositionsX[0] = EnemyAt.X + 1f;   // a 1 m drift keeps the sensor where it is
            d.Step();
            Assert.Equal(EnemyAt, Cover().ContextPoint1);
            mem = ref d.Repo.GetComponentRW<TargetMemory>(d.Self);
            mem.PositionsX[0] = EnemyAt.X + 5f;   // seen 5 m away: re-pointed
            d.Step();
            Assert.Equal(EnemyAt + new Vector3(5f, 0f, 0f), Cover().ContextPoint1);

            var heard = new Vector3(26f, 6f, 0f);
            mem = ref d.Repo.GetComponentRW<TargetMemory>(d.Self);
            mem.LastSeenTick[0] = 10;
            mem.EntityIds[1] = -7;
            mem.Anonymous[1] = 1;
            mem.Freshness[1] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
            mem.Modalities[1] = (byte)SensorModality.Acoustic;
            mem.PositionsX[1] = heard.X; mem.PositionsY[1] = heard.Y; mem.PositionsZ[1] = heard.Z;
            mem.LastSeenTick[1] = 50;
            mem.Count = 2;
            d.Step();
            Assert.Equal(heard, Cover().ContextPoint1);   // the freshest evidence (D13) is where he is looked for
        }

        /// <summary>
        /// ⭐⭐ <c>P8_R2</c> — <c>CE-3144</c>: a used-up position with nowhere else to go keeps fighting from it — it never walks to the
        /// map's ORIGIN. 📐 Found live (the in-process duel): <c>EqsCognitiveBuffer.GetSpanRO()</c> is all 16 slots, the ones past
        /// <c>Count</c> are (0,0,0) score 0, and once the real windows were burned the empty slot won — A walked 140 m to (0,0,0).
        /// 🔴 Red-proof: drop the <c>[..buffer.Count]</c> slice in <c>PickHide</c> and the last move is (0,0,0).
        /// </summary>
        [Fact]
        public void P8_R2_AUsedUpPositionWithNowhereElse_NeverPicksAnEmptyAnswerSlot()
        {
            var d = new Duel(Window(exposuresPerPosition: 2) with { BurnHeat = 99f, ReuseHeat = 98f });
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (10f, 10f, StanceId.Crouched));
            for (int exposure = 0; exposure < 4; exposure++)
            {
                Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
                Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            }
            Assert.All(d.Moves, m => Assert.Equal(new Vector3(10f, 10f, 0f), m));
        }

        /// <summary>
        /// ⭐⭐⭐ <c>P7_R2</c> — D12, SUPPRESS AND BOUND (B in the street, <c>SuppressBeforeRelocate</c>): the cover is used up, so the
        /// unit picks the next one, but first steps out and fires a suppressive burst (<c>FireAtPoint</c>, no aim time) at the enemy's
        /// freshest spot, and then RUNS to the new cover — never back to the used one. ⭐ Under fire it stays down: a near miss holds the
        /// bound until <c>SuppressedSeconds</c> pass. 🔴 Red-proof: drop the <c>Bounding</c> branch in <c>Recover</c> and the unit walks
        /// back to (10,10) before moving on.
        /// </summary>
        [Fact]
        public void P7_R2_SuppressAndBound_BurstsThenRunsToTheNextCover_StayingDownUnderFire()
        {
            var d = new Duel(Street() with { SuppressBeforeRelocate = 1, ExposuresPerPosition = 1, BurnHeat = 99f, ReuseHeat = 98f });
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (10f, 10f, StanceId.Crouched), (20f, 10f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            d.Step();
            d.Answer(PeekAndFireNodes.PeekSite, 7, (10f, 12f, null));
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));       // the one exposure this cover allows
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));

            // under fire: no coming up, not even to suppress
            d.Sense(SensorChange.NearMiss);
            int moves = d.Moves.Count;
            d.Run(2.5);
            Assert.Equal(PeekPhase.Hidden, d.Ws.Phase);
            Assert.Equal(moves, d.Moves.Count);
            Assert.Equal(1, d.Ws.Bounding);                                      // the next cover is already picked

            Assert.Equal(PeekPhase.Blind, d.RunUntil(PeekPhase.Blind));         // out to the peek point, a burst — no aim time
            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide));

            Assert.Equal(new[]
            {
                new Vector3(10f, 10f, 0f), new Vector3(10f, 12f, 0f), new Vector3(10f, 10f, 0f),   // the exposure
                new Vector3(10f, 12f, 0f), new Vector3(20f, 10f, 0f),                              // out, burst, RUN to the next cover
            }, d.Moves);
            Assert.Equal(2, d.Fires.Count);
            Assert.Equal(CombatConstants.ActionIdAimAndFire, d.Fires[0].Action);
            Assert.Equal(CombatConstants.ActionIdFireAtPoint, d.Fires[1].Action);
            Assert.Equal(EnemyAt + new Vector3(0f, 0f, 1f), d.Fires[1].Point);
            Assert.Equal(0, d.Ws.Bounding);
            Assert.Equal(new Vector3(20f, 10f, 0f), d.Ws.HidePoint);
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
            // ⭐ CE-1203 (§5e FL-3) — the Actions row: the peek-and-fire slots and phase are on their own layer (the gizmo is keyed on
            //   SimTransform, which this duel's unit lacks — give it one where it stands)
            if (!d.Repo.HasComponent<SimTransform>(d.Self))
            {
                if (!d.Repo.IsComponentTypeRegistered<SimTransform>()) d.Repo.RegisterComponent<SimTransform>();
                d.Repo.AddComponent(d.Self, new SimTransform());
            }
            Fdp.TestShared.GizmoLayerProbe.AssertOnItsOwnLayer(dr => new PeekAndFireGizmo().Draw(d.Repo, d.Self, dr), Fdp.Toolkit.Diagnostics.Gizmos.DebugTraceLayers.Actions);
        }
            // ── CE-3158 G1 — the peek is chosen from the POINT (📄 docs/DESIGN_Peek_And_Fire.md §10.5) ─────────────────────────────

        /// <summary>
        /// ⭐⭐ <c>G1_R1</c> — A COVER POINT, CROUCHED (a low wall): the rifleman hides crouched at it and comes up ONE STANCE TALLER on
        /// the same spot — no step-out sensor, one move. 🔴 Red-proof: keyed on the template (CE-3136 D7), the cover template meant a
        /// step peek, so the unit stood waiting for a step-out point and never fired.
        /// </summary>
        [Fact]
        public void G1_R1_CrouchedCover_HidesCrouched_ExposesStandingOnTheSpot()
        {
            var d = new Duel(Rifleman());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(0, d.Ws.PeekIsStep);
            Assert.Equal(StanceId.Crouched, d.Stance);

            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            Assert.Equal(StanceId.Standing, d.Stance);
            Assert.True(d.Sensor(PeekAndFireNodes.PeekSite).IsNull, "a stance peek needs no step-out sensor");
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(StanceId.Crouched, d.Stance);
            Assert.Equal(new Vector3(4f, 1f, 0f), Assert.Single(d.Moves));
            Assert.Equal(3, d.AimedRounds);
        }

        /// <summary>⭐ <c>G1_R2</c> — a PRONE cover (a kerb, a slit) comes up to a crouch, not to standing.</summary>
        [Fact]
        public void G1_R2_ProneCover_ExposesCrouched()
        {
            var d = new Duel(Rifleman());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Prone));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(StanceId.Prone, d.Stance);
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            Assert.Equal(StanceId.Crouched, d.Stance);
        }

        /// <summary>
        /// ⭐⭐ <c>G1_R3</c> — a cover ALREADY STANDING (a full-height wall, a corner) has nothing taller to come up to ⇒ STEP PEEK:
        /// out to the step-peek sensor's point, standing, and back.
        /// </summary>
        [Fact]
        public void G1_R3_StandingCover_StepsOut()
        {
            var d = new Duel(Rifleman());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (10f, 10f, StanceId.Standing));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(1, d.Ws.PeekIsStep);
            d.Step();
            d.Answer(PeekAndFireNodes.PeekSite, 7, (10f, 12f, null));
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            Assert.Equal(new[] { new Vector3(10f, 10f, 0f), new Vector3(10f, 12f, 0f) }, d.Moves);
        }

        /// <summary>⭐⭐ <c>G1_R4</c> — a unit with NO STANCE (no <see cref="StanceIntent"/>) cannot come up ⇒ step peek even at a crouched
        /// cover.</summary>
        [Fact]
        public void G1_R4_NoStanceSystem_StepsOut()
        {
            var d = new Duel(Rifleman());
            d.Repo.RemoveComponent<StanceIntent>(d.Self);
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(1, d.Ws.PeekIsStep);
        }

        /// <summary>
        /// ⭐⭐ <c>G1_R5</c> — THE POINT DECIDES, NOT THE QUERY: a WINDOW answered by the cover template (a template that finds both)
        /// is a stance peek at the window's stance, prone below the sill. 🔴 Red-proof: keyed on the template it was a step peek.
        /// </summary>
        [Fact]
        public void G1_R5_AWindowFoundByTheCoverQuery_IsAStancePeek()
        {
            var d = new Duel(Rifleman()) { Kind = CoverKind.WindowFiring };
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (2f, 3f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(0, d.Ws.PeekIsStep);
            Assert.Equal(StanceId.Prone, d.Stance);
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            Assert.Equal(StanceId.Crouched, d.Stance);
        }

        /// <summary>⭐ <c>G1_R6</c> — <see cref="PeekAndFireParams.PeekStanceOverride"/>: prone behind a low wall, STAND to fire.</summary>
        [Fact]
        public void G1_R6_PeekStanceOverride_StandsUpFromProne()
        {
            var d = new Duel(Rifleman() with { PeekStanceOverride = (byte)(StanceId.Standing + 1) });
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Prone));
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            Assert.Equal(0, d.Ws.PeekIsStep);
            Assert.Equal(StanceId.Standing, d.Stance);
        }
            // ── CE-3158 G2 — the node says when it cannot work (📄 docs/DESIGN_Peek_And_Fire.md §10.5) ───────────────────────────────

        /// <summary>
        /// ⭐⭐ <c>G2_R1</c> — NO COVER: the cover sensor answers nothing ⇒ the node answers <c>Failure</c> once <c>NoCoverSeconds</c>
        /// (3 s) have passed — not before — so the posture can pick something else. 🔴 Red-proof: before G2 it ran forever.
        /// </summary>
        [Fact]
        public void G2_R1_NoCover_FailsAfterNoCoverSeconds()
        {
            var d = new Duel(Rifleman());
            var status = NodeStatus.Running;
            double start = d.Time;
            while (d.Time - start < 10 && status == NodeStatus.Running) status = d.Step();
            Assert.Equal(NodeStatus.Failure, status);
            Assert.InRange(d.Time - start, 3.0, 3.3);
            Assert.True(d.Sensor(PeekAndFireNodes.CoverSite).IsNull, "a failed run releases its sensors");
            Assert.Empty(d.Fires);
        }

        /// <summary>
        /// ⭐⭐ <c>G2_R2</c> — AN UNREACHABLE POINT: a failed move heats it (<c>UnreachableHeat × BurnHeat</c>), so the node takes the next
        /// point instead of re-trying the same one. 🔴 Red-proof: before G2 a failed move only re-asked, and the best point was
        /// picked again forever.
        /// </summary>
        [Fact]
        public void G2_R2_AFailedMove_HeatsThePoint_AndTheNextOneIsTaken()
        {
            var d = new Duel(Rifleman());
            d.Unreachable.Add(new Vector3(4f, 1f, 0f));
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched), (8f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(new[] { new Vector3(4f, 1f, 0f), new Vector3(8f, 1f, 0f) }, d.Moves);
            var m = d.Memory;
            int slot = PositionHeat.Find(ref m, new Vector3(4f, 1f, 0f), 1f);
            Assert.True(slot >= 0, "the failed point is remembered");
            Assert.InRange(PositionHeat.HeatNow(ref m, slot, d.Time, 45f), 1.4f, 1.5f);
        }

        /// <summary>
        /// ⭐⭐ <c>G2_R3</c> — EVERY POINT BURNED: the unit does not go silent — it fires AIMED from where it stands (no move), and goes
        /// to a point again once one cools.
        /// </summary>
        [Fact]
        public void G2_R3_EveryPointBurned_FiresAimedFromWhereItStands()
        {
            var d = new Duel(Rifleman());
            d.Unreachable.Add(new Vector3(4f, 1f, 0f));
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched));
            d.Run(3);
            int moves = d.Moves.Count;
            Assert.Equal(2, moves);   // tried twice — the only point — and burned
            var m = d.Memory;
            Assert.True(PositionHeat.IsBurnedAt(ref m, new Vector3(4f, 1f, 0f), d.Time, HeatRules.Default));
            d.Run(3);
            Assert.Equal(moves, d.Moves.Count);   // stays where it stands
            Assert.Equal(2, d.Ws.Waiting);
            Assert.Contains(d.Fires, f => f.Action == CombatConstants.ActionIdAimAndFire);
            Assert.True(d.AimedRounds > 0, "fights from where it stands, never silent");
        }

        /// <summary>
        /// ⭐ <c>G2_R4</c> — A STEP PEEK WITH NOWHERE TO STEP: no step-out point for <c>NoCoverSeconds</c> ⇒ this cover cannot be fought
        /// from ⇒ to the next one. 🔴 Red-proof: before G2 the unit crouched behind it forever.
        /// </summary>
        [Fact]
        public void G2_R4_StepPeekWithNoStepOutPoint_MovesToTheNextCover()
        {
            var d = new Duel(Rifleman());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (10f, 10f, StanceId.Standing), (20f, 10f, StanceId.Standing));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.Equal(1, d.Ws.PeekIsStep);
            d.Step();
            d.Answer(PeekAndFireNodes.PeekSite, 7);   // the step-peek sensor answers nothing
            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide, maxSeconds: 10));
            Assert.Equal(new Vector3(20f, 10f, 0f), d.Moves[^1]);
        }
            // ── CE-3158 G3 — the ROE decides whether the unit comes up at all ─────────────────────────────────────────────────────

        private static void SetRoe(Duel d, RoeFire fire)
        {
            if (!d.Repo.IsComponentTypeRegistered<Roe>()) d.Repo.RegisterComponent<Roe>();
            var roe = new Roe { Fire = fire };
            if (d.Repo.HasComponent<Roe>(d.Self)) d.Repo.SetComponent(d.Self, roe); else d.Repo.AddComponent(d.Self, roe);
        }

        /// <summary>
        /// ⭐⭐ <c>G3_R4</c> — HOLD FIRE ⇒ NEVER EXPOSES: the unit takes cover and stays down (= the old TakeCover); no round, no
        /// exposure. 🔴 Red-proof: before G3 the node came up, the executor held the trigger, and the unit stood exposed for nothing.
        /// </summary>
        [Fact]
        public void G3_R4_HoldFire_NeverExposes()
        {
            var d = new Duel(Rifleman());
            SetRoe(d, RoeFire.HoldFire);
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched));
            d.Run(20);
            Assert.Equal(PeekPhase.Hidden, d.Ws.Phase);
            Assert.Equal(0, d.Ws.Exposures);
            Assert.Equal(StanceId.Crouched, d.Stance);
            Assert.Empty(d.Fires);
        }

        /// <summary>
        /// ⭐⭐ <c>G3_R5</c> — RETURN FIRE ⇒ DOWN until fired upon, then UP inside the window (after the B8 suppression, without the
        /// random hide wait), and down again when the window closes.
        /// </summary>
        [Fact]
        public void G3_R5_ReturnFire_ExposesOnlyInsideTheWindow()
        {
            var d = new Duel(Rifleman() with { HideSecondsMin = 30f, HideSecondsMax = 30f });
            SetRoe(d, RoeFire.ReturnFire);
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched));
            d.Run(10);
            Assert.Equal(0, d.Ws.Exposures);   // not fired upon: stays down

            d.Sense(SensorChange.NearMiss);    // fired upon ⇒ the 5 s window opens; B8 keeps it down 3 s, then it comes up
            double at = d.Time;
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed, maxSeconds: 5));
            Assert.InRange(d.Time - at, 3.0, 5.0);   // ⭐ not after the 30 s hide wait
        }
            // ── CE-3158 G4 — one target LOCKED per exposure ─────────────────────────────────────────────────────────────────────────

        /// <summary>A second enemy at <paramref name="at"/>, remembered and seen (slot 1).</summary>
        private static Entity SecondEnemy(Duel d, Vector3 at)
        {
            var e = d.Repo.CreateEntity();
            ref var mem = ref d.Repo.GetComponentRW<TargetMemory>(d.Self);
            mem.EntityIds[1] = (long)e.PackedValue;
            mem.Freshness[1] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
            mem.Modalities[1] = (byte)SensorModality.Visual;
            mem.PositionsX[1] = at.X; mem.PositionsY[1] = at.Y; mem.PositionsZ[1] = at.Z;
            mem.Count = 2;
            ref var t = ref d.Repo.GetComponentRW<ActiveSensorTracks>(d.Self);
            t.EntityIds[1] = (long)e.PackedValue;
            t.Modalities[1] = (byte)SensorModality.Visual;
            t.Count = 2;
            return e;
        }

        /// <summary>Makes the unit a member of a squad whose threat matrix assigned it <paramref name="target"/>.</summary>
        private static void AssignBySquad(Duel d, Entity target)
        {
            var r = d.Repo;
            if (!r.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitRoster>()) r.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
            if (!r.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitSubordinate>()) r.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
            if (!r.IsComponentTypeRegistered<Fdp.Toolkit.Squad.SquadCognitiveState>()) r.RegisterComponent<Fdp.Toolkit.Squad.SquadCognitiveState>();
            var leader = r.CreateEntity();
            var roster = new Fdp.Core.CommandHierarchy.UnitRoster();
            int idx = Fdp.Core.CommandHierarchy.UnitRoster.Add(ref roster, d.Self);
            r.AddComponent(leader, roster);
            var squad = new Fdp.Toolkit.Squad.SquadCognitiveState();
            squad.Assignment.SetAssignment(idx, target.PackedValue);
            r.AddComponent(leader, squad);
            r.AddComponent(d.Self, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = leader });
        }

        /// <summary>
        /// ⭐⭐ <c>G4_R1</c> — THE SQUAD'S ASSIGNED TARGET IS THE ONE SHOT AT: two enemies, both seen; the squad assigned the second — the
        /// exposure's sight check and its aimed action are both on it. 🔴 Red-proof: before G4 the fire step re-ranked by itself and
        /// shot at the top threat whatever the squad had assigned.
        /// </summary>
        [Fact]
        public void G4_R1_TheSquadsAssignedTarget_IsTheOneShotAt()
        {
            var d = new Duel(Rifleman());
            var second = SecondEnemy(d, new Vector3(25f, 5f, 0f));
            AssignBySquad(d, second);
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            Assert.Equal(second, Assert.Single(d.AimedAt));
        }

        /// <summary>⭐ <c>G4_R2</c> — without a squad the top threat is the locked target, and the exposure's shot goes at it.</summary>
        [Fact]
        public void G4_R2_ALoneSoldier_ShootsTheTopThreat()
        {
            var d = new Duel(Rifleman());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            Assert.Equal(d.Enemy, Assert.Single(d.AimedAt));
        }

        /// <summary>
        /// ⭐⭐ <c>G4_R3</c> — A SHOT HEARD ELSEWHERE IS ANOTHER ENEMY: the burst at a hidden locked target goes to its remembered spot,
        /// not to a fresher heard shot 40 m away; a heard shot NEAR it (the same man moved) does move the burst (D13 kept).
        /// </summary>
        [Fact]
        public void G4_R3_AHeardShotMovesTheBurst_OnlyNearTheLockedTarget()
        {
            var d = new Duel(Rifleman());
            var t = new ThreatAim(d.Enemy, 0, default);
            ref var mem = ref d.Repo.GetComponentRW<TargetMemory>(d.Self);
            mem.EntityIds[1] = -42; mem.Anonymous[1] = 1; mem.PositionsX[1] = 20f; mem.PositionsY[1] = 40f; mem.LastSeenTick[1] = 99; mem.Count = 2;
            Assert.True(TargetMemory.IsAnonymous(in mem, 1));
            Assert.True(PeekAndFireNodes.FreshestEvidence(d.Repo, d.Self, in t, out var far, out _, 15f));
            Assert.Equal(EnemyAt, far);                                   // 40 m away: not him

            mem.PositionsY[1] = 6f;                                        // 6 m from where he was: him, moved
            Assert.True(PeekAndFireNodes.FreshestEvidence(d.Repo, d.Self, in t, out var near, out _, 15f));
            Assert.Equal(new Vector3(20f, 6f, 0f), near);
        }
            // ── CE-3158 G5 — squad-mates keep off each other's cover (📄 docs/DESIGN_Peek_And_Fire.md §10.7) ──────────────────────

        /// <summary>A friend (same force as the unit — both default) holding a claim on <paramref name="hide"/> until <paramref name="until"/>.</summary>
        private static Entity FriendClaims(Duel d, Vector3 hide, double until, ForceId? force = null)
        {
            var f = d.Repo.CreateEntity();
            d.Repo.AddComponent(f, new Fdp.Toolkit.Combat.Components.CoverClaim { Hide = hide, Peek = hide, Until = until });
            if (force is { } fid) d.Repo.AddComponent(f, new EntityInfo { ForceId = fid });
            return f;
        }

        /// <summary>
        /// ⭐⭐⭐ <c>G5_R1</c> — TWO RIFLEMEN, ONE GOOD POINT: a squad-mate already claims the best point, so this one takes the next — and
        /// publishes its OWN claim the moment it commits (the move), refreshed while it holds it. 🔴 Red-proof: before G5 both took
        /// the best point and stacked on one spot.
        /// </summary>
        [Fact]
        public void G5_R1_AFriendsClaimedPoint_IsSkipped_AndMyOwnClaimIsPublished()
        {
            var d = new Duel(Rifleman());
            FriendClaims(d, new Vector3(4f, 1f, 0f), until: 1e9);
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched), (9f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide));
            Assert.Equal(new Vector3(9f, 1f, 0f), d.Moves[^1]);

            var mine = d.Repo.GetComponentRO<Fdp.Toolkit.Combat.Components.CoverClaim>(d.Self);
            Assert.Equal(new Vector3(9f, 1f, 0f), mine.Hide);
            Assert.InRange(mine.Until - d.Time, 1.5, 2.1);   // live, refreshed every tick (TTL 2 s)
            d.Run(5);
            Assert.InRange(d.Repo.GetComponentRO<Fdp.Toolkit.Combat.Components.CoverClaim>(d.Self).Until - d.Time, 1.5, 2.1);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2275</c> D-2a (PC-14, R-2047) — A POSTED MAN HIDES ONLY ON HIS POST: the better point 5 m off his post is passed over for
        /// the one on it; with his post elsewhere, his ALTERNATE is taken. ✅ Red-proof: the post filter removed ⇒ he takes the best point.
        /// </summary>
        [Fact]
        public void CE2275_APostedMan_HidesOnlyOnHisPost_OrOnHisAlternate()
        {
            var d = new Duel(Rifleman() with { Anchor = new Vector3(9f, 1f, 0f), AnchorRadius = 4f });
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched), (9f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide));
            Assert.Equal(new Vector3(9f, 1f, 0f), d.Moves[^1]);

            var a = new Duel(Rifleman() with
            {
                Anchor = new Vector3(30f, 0f, 0f), AnchorRadius = 4f, AnchorAlternate = new Vector3(9f, 1f, 0f), AnchorHasAlternate = 1,
            });
            a.Step();
            a.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched), (9f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.MoveToHide, a.RunUntil(PeekPhase.MoveToHide));
            Assert.Equal(new Vector3(9f, 1f, 0f), a.Moves[^1]);
        }

        /// <summary>
        /// ⭐ <c>CE-2275</c> D-2a (PC-14: "a man who finds none fights from where he is") — every answered point is off his post: he does not
        /// leave it, and fires AIMED from where he stands (G2's every-point-burned path). ✅ Red-proof: the post filter removed ⇒ he moves.
        /// </summary>
        [Fact]
        public void CE2275_NoPointOnHisPost_HeStaysAndFightsFromWhereHeStands()
        {
            var d = new Duel(Rifleman() with { Anchor = new Vector3(30f, 0f, 0f), AnchorRadius = 4f });
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched), (9f, 1f, StanceId.Crouched));
            d.Run(6);
            Assert.Empty(d.Moves);
            Assert.Equal(2, d.Ws.Waiting);
            Assert.True(d.AimedRounds > 0, "fights from where he stands, never silent");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2275</c> D-2i (PC-14's <c>Allow</c> half, DA-49) — a POSTED man whose threat lies outside his sector looks for his
        /// hide points toward his SECTOR: his cover sensor is aimed at his sector's point, not at the threat (a window that sees the threat
        /// would face away from his arc). A threat inside his arc, a man with no post, or a close threat that just fired at him (self-defence)
        /// keep the aim on the threat. ✅ Red-proofs: the re-aim removed ⇒ the sensor looks east at the threat; the Allow test skipped ⇒
        /// the sector even for a threat inside it.
        /// </summary>
        [Fact]
        public void CE2275_D2i_APostedMansHideSearch_LooksAtHisSector_WhenTheThreatIsOutsideIt()
        {
            var d = new Duel(Rifleman() with { Anchor = Vector3.Zero, AnchorRadius = 4f });
            if (!d.Repo.IsComponentTypeRegistered<EngagementArea>()) d.Repo.RegisterComponent<EngagementArea>();
            d.Repo.AddComponent(d.Self, new EngagementArea
            {
                ArcSet = 1, ArcOrigin = Vector3.Zero, LeftBearing = 330f, RightBearing = 30f, MaxRange = 200f, SetBy = BehaviorOrigin.Operator,
            });
            d.Repo.AddComponent(d.Enemy, new SimTransform { Position = new Vector3(80f, 0f, 0f), Rotation = Quaternion.Identity });   // east, beyond 50 m
            var threat = new ThreatAim(d.Enemy, 0, default);

            var aim = PeekAndFireNodes.PostedAim(d.Repo, d.Self, in d.P, in threat);
            Assert.Equal(PeekAndFireNodes.SectorAimId, aim.HeardId);
            Assert.True(aim.IsPoint);
            Assert.Equal(0f, aim.Point.X, 1);
            Assert.Equal(100f, aim.Point.Y, 1);   // the arc's middle bearing (north) at 100 m (no range band)

            d.Step();   // the node itself: its cover sensor looks at the sector
            ref readonly var cover = ref d.Repo.GetComponentRO<EqsSensor>(d.Sensor(PeekAndFireNodes.CoverSite));
            Assert.True(cover.ContextSlot1.IsNull);
            Assert.NotEqual(0, cover.ContextPointMask & EqsSensor.Point1Bit);
            Assert.Equal(100f, cover.ContextPoint1.Y, 1);

            var unposted = d.P with { AnchorRadius = 0f };
            Assert.Equal(threat, PeekAndFireNodes.PostedAim(d.Repo, d.Self, in unposted, in threat));   // no post: as before

            d.Repo.GetComponentRW<SimTransform>(d.Enemy).Position = new Vector3(0f, 80f, 0f);           // north, inside his arc
            Assert.Equal(threat, PeekAndFireNodes.PostedAim(d.Repo, d.Self, in d.P, in threat));

            d.Repo.GetComponentRW<SimTransform>(d.Enemy).Position = new Vector3(20f, 0f, 0f);           // close, east, and firing at him
            if (!d.Repo.HasComponent<SimTransform>(d.Self)) d.Repo.AddComponent(d.Self, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            d.Repo.GetComponentRW<RecentSenses>(d.Self).Record(SensorChange.NearMiss, d.Time);
            Assert.Equal(threat, PeekAndFireNodes.PostedAim(d.Repo, d.Self, in d.P, in threat));
        }

        /// <summary>⭐⭐ <c>G5_R2</c> — a claim that LAPSED (its brain stopped refreshing it) frees the point: no cleanup system needed.</summary>
        [Fact]
        public void G5_R2_AnExpiredClaim_FreesThePoint()
        {
            var d = new Duel(Rifleman());
            FriendClaims(d, new Vector3(4f, 1f, 0f), until: -1);
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched), (9f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide));
            Assert.Equal(new Vector3(4f, 1f, 0f), d.Moves[^1]);
        }

        /// <summary>⭐ <c>G5_R3</c> — an ENEMY's claim is not mine to respect (claims are per force).</summary>
        [Fact]
        public void G5_R3_AnotherForcesClaim_IsIgnored()
        {
            var d = new Duel(Rifleman());
            d.Repo.AddComponent(d.Self, new EntityInfo { ForceId = ForceId.Friend });
            FriendClaims(d, new Vector3(4f, 1f, 0f), until: 1e9, force: ForceId.Hostile);
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched), (9f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide));
            Assert.Equal(new Vector3(4f, 1f, 0f), d.Moves[^1]);
        }

        /// <summary>⭐ <c>G5_R4</c> — R-252: surfaces stack — a claim on the FLOOR ABOVE the same spot is another cover (3-D radius).</summary>
        [Fact]
        public void G5_R4_AClaimOnTheFloorAbove_IsAnotherPoint()
        {
            var d = new Duel(Rifleman());
            FriendClaims(d, new Vector3(4f, 1f, 3f), until: 1e9);
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched), (9f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide));
            Assert.Equal(new Vector3(4f, 1f, 0f), d.Moves[^1]);
        }

        /// <summary>⭐ <c>G5_R5</c> — leaving the node frees the point AT ONCE (the deactivator), not after the TTL.</summary>
        [Fact]
        public void G5_R5_LeavingTheNode_ReleasesTheClaim()
        {
            var d = new Duel(Rifleman());
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            PeekAndFireNodes.Deactivate_PeekAndFire(ref d.P, ref d.Ws, d.Self, d.Repo);
            Assert.Equal(0d, d.Repo.GetComponentRO<Fdp.Toolkit.Combat.Components.CoverClaim>(d.Self).Until);
        }
            // ── CE-3158 G8 — "off" is expressible; an exposure outlasts the aim ───────────────────────────────────────────────────

        /// <summary>⭐⭐ <c>G8_R1</c> — <see cref="PeekDisable.BlindFire"/>: an unseen enemy gets NO blind burst — the unit comes up, waits
        /// the grace time and goes back down. 🔴 Red-proof: without the bit (a zero <c>BlindRounds</c> means the default 3) a burst flies.</summary>
        [Fact]
        public void G8_R1_BlindFireOff_NoBurstAtAnUnseenEnemy()
        {
            var d = new Duel(Rifleman() with { Disable = PeekDisable.BlindFire }, seen: false);
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched));
            d.Run(12);
            Assert.True(d.Ws.Exposures >= 1, "it still comes up to look");
            Assert.DoesNotContain(d.Fires, f => f.Action == CombatConstants.ActionIdFireAtPoint);
        }

        /// <summary>⭐ <c>G8_R2</c> — <see cref="PeekDisable.HeatPenalty"/>: a WARM (not burned) best point keeps its rank. 🔴 Red-proof:
        /// with the penalty, heat 2 × 0.3 drops it below the second point.</summary>
        [Fact]
        public void G8_R2_HeatPenaltyOff_AWarmBestPointKeepsItsRank()
        {
            foreach (var off in new[] { false, true })
            {
                var d = new Duel(Rifleman() with { Disable = off ? PeekDisable.HeatPenalty : PeekDisable.None });
                var mem = new FiringPositionMemory();
                PositionHeat.Add(ref mem, new Vector3(4f, 1f, 0f), 2.0f, 0d, HeatRules.Default, exposure: true);
                UnitMemory.Set(d.Repo, d.Self, mem);
                d.Step();
                d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched), (8f, 1f, StanceId.Crouched));
                Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide));
                Assert.Equal(off ? new Vector3(4f, 1f, 0f) : new Vector3(8f, 1f, 0f), d.Moves[^1]);
            }
        }

        /// <summary>⭐ <c>G8_R3</c> — <see cref="PeekDisable.SuppressAndBound"/> overrides <c>SuppressBeforeRelocate</c>: a used-up position
        /// is left WITHOUT a suppressive burst.</summary>
        [Fact]
        public void G8_R3_SuppressAndBoundOff_RelocatesWithoutABurst()
        {
            var d = new Duel(Street() with { SuppressBeforeRelocate = 1, ExposuresPerPosition = 1, BurnHeat = 99f, ReuseHeat = 98f,
                                             Disable = PeekDisable.SuppressAndBound });
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (10f, 10f, StanceId.Crouched), (20f, 10f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            d.Step();
            d.Answer(PeekAndFireNodes.PeekSite, 7, (10f, 12f, null));
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            Assert.Equal(PeekPhase.MoveToHide, d.RunUntil(PeekPhase.MoveToHide));
            Assert.Equal(0, d.Ws.Bounding);
            Assert.DoesNotContain(d.Fires, f => f.Action == CombatConstants.ActionIdFireAtPoint);
        }

        /// <summary>⭐⭐ <c>G8_R4</c> — an aimed exposure lasts at least 2 × the weapon's aim time (fallback 0.8 s ⇒ 1.6 s) even when
        /// <c>ExposeSeconds</c> is shorter — else it ends before the first round leaves. 🔴 Red-proof: with <c>ExposeSeconds</c> alone it
        /// ends at 0.5 s.</summary>
        [Fact]
        public void G8_R4_AnAimedExposure_OutlastsTwiceTheAimTime()
        {
            var d = new Duel(Rifleman() with { ExposeSeconds = 0.5f, RoundsPerExposure = 1000, GraceSeconds = 5f });
            d.Step();
            d.Answer(PeekAndFireNodes.CoverSite, 5, (4f, 1f, StanceId.Crouched));
            Assert.Equal(PeekPhase.Aimed, d.RunUntil(PeekPhase.Aimed));
            double up = d.Time;
            Assert.Equal(PeekPhase.Hidden, d.RunUntil(PeekPhase.Hidden));
            Assert.InRange(d.Time - up, 1.5, 1.9);
        }
        // ── ⭐ CE-2274 DA-C — proficiency in the cycle (docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8b) ──────────

        private static void Skill(Duel d, float value)
        {
            var c = d.Repo.HasComponent<CombatProficiency>(d.Self) ? d.Repo.GetComponentRO<CombatProficiency>(d.Self) : new CombatProficiency();
            c.Authored = value;
            c.Set |= CombatProficiency.AuthoredSet;
            if (d.Repo.HasComponent<CombatProficiency>(d.Self)) d.Repo.SetComponent(d.Self, c);
            else d.Repo.AddComponent(d.Self, c);
        }

        /// <summary>⭐ DA-2's promise: with no proficiency, or a Regular one, the cycle runs on EXACTLY the given values — every existing
        /// rail and demo is unchanged.</summary>
        [Fact]
        public void CE2274_Skilled_NoneOrRegular_IsExactlyTheGivenValues()
        {
            var d = new Duel(Window());
            var p = PeekAndFireNodes.Effective(Window());
            Assert.Equal(p, PeekAndFireNodes.Skilled(in p, d.Repo, d.Self));
            Skill(d, ProficiencyOf.Regular);
            Assert.Equal(p, PeekAndFireNodes.Skilled(in p, d.Repo, d.Self));
        }

        /// <summary>⭐ DA-2 / PC-15 by tier — exposure × (1.8 · 1.4 · 1 · 0.85 · 0.7); bursts: Militia doubles them, Untrained empties his
        /// magazine (30); who switches: Untrained / Militia never by count, Veteran one sooner, Elite after one.
        /// ✅ Red-proof: the tier switch removed ⇒ the militiaman leaves after three.</summary>
        [Fact]
        public void CE2274_Skilled_ByTier_Exposure_Bursts_AndWhoSwitches()
        {
            var d = new Duel(Window());
            var p = PeekAndFireNodes.Effective(Window());
            PeekAndFireParams At(float value) { Skill(d, value); return PeekAndFireNodes.Skilled(in p, d.Repo, d.Self); }

            var untrained = At(ProficiencyOf.Untrained);
            Assert.Equal(p.ExposeSeconds * 1.8f, untrained.ExposeSeconds, 3);
            Assert.Equal(30, untrained.RoundsPerExposure);
            Assert.Equal(30, untrained.BlindRounds);
            Assert.Equal(int.MaxValue, untrained.ExposuresPerPosition);

            var militia = At(ProficiencyOf.Militia);
            Assert.Equal(p.ExposeSeconds * 1.4f, militia.ExposeSeconds, 3);
            Assert.Equal(p.RoundsPerExposure * 2, militia.RoundsPerExposure);
            Assert.Equal(int.MaxValue, militia.ExposuresPerPosition);

            var veteran = At(ProficiencyOf.Veteran);
            Assert.Equal(p.ExposeSeconds * 0.85f, veteran.ExposeSeconds, 3);
            Assert.Equal(p.RoundsPerExposure, veteran.RoundsPerExposure);
            Assert.Equal(p.ExposuresPerPosition - 1, veteran.ExposuresPerPosition);

            var elite = At(ProficiencyOf.Elite);
            Assert.Equal(p.ExposeSeconds * 0.7f, elite.ExposeSeconds, 3);
            Assert.Equal(1, elite.ExposuresPerPosition);
        }

        /// <summary>⭐ DA-2 — the reaction delay: up at the window, an UNTRAINED man fires his first shot at a new target ≥ 1.2 s after he
        /// sees it; at the same target next time he fires at once; a Regular fires at once the first time.
        /// ✅ Red-proof: the delay gate removed ⇒ the untrained man fires at once.</summary>
        [Fact]
        public void CE2274_ReactionDelay_BeforeTheFirstShotAtANewTarget_Only()
        {
            double FirstShotAfterExposing(Duel d)
            {
                int before = d.Fires.Count;
                Assert.Equal(PeekPhase.Expose, d.RunUntil(PeekPhase.Expose));
                double up = d.Time;
                for (int i = 0; i < 100 && d.Fires.Count == before; i++) d.Step();
                Assert.True(d.Fires.Count > before, "he fires during the exposure");
                return d.Time - up;
            }

            var regular = new Duel(Window());
            regular.Step();
            regular.Answer(PeekAndFireNodes.CoverSite, 5, (2f, 3f, StanceId.Crouched));
            Assert.InRange(FirstShotAfterExposing(regular), 0.0, 0.31);

            var untrained = new Duel(Window());
            Skill(untrained, ProficiencyOf.Untrained);
            untrained.Step();
            untrained.Answer(PeekAndFireNodes.CoverSite, 5, (2f, 3f, StanceId.Crouched));
            Assert.InRange(FirstShotAfterExposing(untrained), 1.2, 1.6);                  // the first look at him
            Assert.Equal(PeekPhase.Hidden, untrained.RunUntil(PeekPhase.Hidden));
            Assert.InRange(FirstShotAfterExposing(untrained), 0.0, 0.31);                 // the same man again: no delay
        }
    }
}
