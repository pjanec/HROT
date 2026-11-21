using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Creatures;
using Fdp.Toolkit.Determinism;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Attributes;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-4623</c> AB-4 (docs/designs/animals/DESIGN_Animals.md AN-1, AR-9, AR-21) — <c>AnimalHsm</c>, EVERY animal's one brain; the species is
    /// DATA (its <see cref="Fdp.Toolkit.Tkb.Domain.AnimalTemperamentDto"/>, read once at start). <b>Graze</b> (walk a little, stand a while — round
    /// its herd's grazing spot, its home, or where it was placed) · <b>Rest</b> (lies down at night, a herd's whole) · <b>Alert</b> (uneasy: head up;
    /// frightened: moves off from the threat) · <b>Flee</b> (panic: runs its flee distance away from where the fright came from, at its flee speed —
    /// the herd runs the same way, its contagion passes the fright on) · <b>Recover</b> (stands looking back a while, then grazes again — its herd's
    /// next spot is picked AWAY from the fright) · <b>Casualty</b> (down: lies there). Its fear is <see cref="Fear"/>, kept by the ONE fear system
    /// and the <see cref="HerdSystem"/>; this machine only reads it and writes its own <see cref="AnimalState.Mode"/>. ⭐ The species plays (the
    /// guard dog, the cat, the riding horse — AB-5) are states of this same machine, not other brains.
    /// </summary>
    public static class AnimalBehavior
    {
        /// <summary>Uneasy at this fear (band 1).</summary>
        public const float UneasyAt = 0.15f;
        /// <summary>Frightened (band 2): it moves off.</summary>
        public const float FrightenedAt = 0.4f;
        /// <summary>Panic (band 3): it runs.</summary>
        public const float PanicAt = 0.75f;
        /// <summary>Calm again below this.</summary>
        public const float CalmBelow = 0.1f;
        /// <summary>A frightened animal moves this far off (m).</summary>
        public const float MoveOffMetres = 15f;
        /// <summary>⭐ CE-4649 part C ② — a frightened bird flies this far off (m): beyond <c>BirdFlightModel.DescendWithinM</c>, so it climbs to its cruise height.</summary>
        public const float BirdTakeOffMetres = 90f;
        /// <summary>A lone animal grazes this far round where it was placed (m).</summary>
        public const float LoneGrazeMetres = 15f;
        /// <summary>A herd member stands this far round its herd's spot, plus up to <see cref="LooseSpreadMetres"/> × (1 − cohesion).</summary>
        public const float TightSpreadMetres = 3f, LooseSpreadMetres = 20f;
        public const float PauseMinSeconds = 5f, PauseMaxSeconds = 20f;
        /// <summary>After a run it stands looking back this share of its recovery time.</summary>
        public const float LookBackShare = 0.25f;
        public const float ArrivalMetres = 2f;

        [StructLayout(LayoutKind.Sequential)]
        public struct AnimalParams
        {
            public float Walk, FleeSpeed, FleeMetres, Cohesion;
            /// <summary>⭐ S0 — the recovery time (a duration, one float: the layout is unchanged).</summary>
            public SimDuration Recovery;
            public byte Fearless;
            /// <summary>⭐ AB-5 — guards its ground (the dog); a bird (a flock or a lone bird: perches, flies off).</summary>
            public byte Territorial, Bird;
            /// <summary>⭐ CE-4649 part D — the flee style (1 = zigzag), freezes before it bolts (AN-21b), and the hunting-dog play (AN-21f).</summary>
            public byte Zigzag, Freezes, Ranges;
            /// <summary>⭐ AB-8 — its mean interval between calls at rest (s); 0 = silent.</summary>
            public SimDuration CallInterval;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GrazeState
        {
            public byte Walking, HasOrigin;
            public int Legs;
            /// <summary>⭐ AB-8 — the last call number it made (the closed-form schedule's k), −1 none.</summary>
            public long LastCall;
            /// <summary>⭐ CE-4649 part D (AN-21f) — the hunting dog's play: 0 ranging · 1 chasing a flushed animal · 2 coming back; when the chase ends; which side the next sweep runs.</summary>
            public byte Phase, Side;
            public SimInstant ChaseUntil;
            public SimInstant PauseUntil;
            public Vector3 Target, Origin;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FleeState
        {
            public byte Moving, Arrived;
            public SimInstant Since;
            public Vector3 Target;
            /// <summary>⭐ CE-4649 part D (AN-21c) — the zigzag chain: legs run, the side of the last jink, the metres run, the bent line of the run.</summary>
            public byte Legs, Side;
            public float Run;
            public Vector3 Base;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RecoverState
        {
            public byte Started;
            public SimInstant Until;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AnimalBlackboard
        {
            public AnimalParams Params;
            public GrazeState Graze;
            public FleeState Flee;
            public FleeState MoveOff;
            public RecoverState Recover;
            public GuardState Guard;
        }

        /// <summary>⭐ AB-5 — the guard dog's working memory: its next re-aim and its next bite.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct GuardState
        {
            public SimInstant NextAimAt, NextBiteAt;
        }

        /// <summary>The species' numbers from its temperament (the ONE table, AR-21); a type with none walks at 1 m/s and runs 30 m.</summary>
        [BehaviorResolver(BehaviorNames.AnimalHsm)]
        public static void Resolve(in NoParams authored, ref AnimalBlackboard block, EntityRepository world, Entity self)
        {
            var t = AnimalFear.TemperamentOf(world, self);
            block = new AnimalBlackboard
            {
                Params = new AnimalParams
                {
                    Walk = t is { WalkSpeed: > 0f } ? t.WalkSpeed : 1f,
                    FleeSpeed = t is { FleeSpeed: > 0f } ? t.FleeSpeed : 4f,
                    FleeMetres = t is { FleeDistanceM: > 0f } ? t.FleeDistanceM : 30f,
                    Recovery = t != null && t.Recovery > 0f ? t.Recovery : 30f,
                    Cohesion = t?.HerdCohesion ?? 0f,
                    Fearless = (byte)(t is { Fearless: true } ? 1 : 0),
                    Territorial = (byte)(t is { Territorial: true } ? 1 : 0),
                    Bird = (byte)(t != null && (t.Species == (byte)AnimalSpecies.BirdFlock || t.Species == (byte)AnimalSpecies.Bird || t.Species == (byte)AnimalSpecies.Duck || t.Species == (byte)AnimalSpecies.DuckFlock) ? 1 : 0),
                    CallInterval = t?.CallInterval ?? SimDuration.Zero,
                    Zigzag = (byte)(t is { FleeStyle: 1 } ? 1 : 0), Freezes = (byte)(t is { Freezes: true } ? 1 : 0), Ranges = (byte)(t is { Ranges: true } ? 1 : 0),
                },
            };
        }

        /// <summary>📄 AN-1: Graze ⇄ Rest (the herd's night); Graze / Rest ⇒ Alert (uneasy) ⇒ Flee (panic); Alert ⇒ Graze (calm); Flee ⇒ Recover once
        /// it has run and is below panic; Recover ⇒ Flee (a new panic) or Graze (calm, looked back); down ⇒ Casualty from every state, first.</summary>
        [Behavior(BehaviorNames.AnimalHsm, Params = typeof(NoParams), Categories = BehaviorCategory.Civilian)]
        [HsmDefinition(BehaviorNames.AnimalHsm, Curated = true, Group = "Library/Animals", ParamsType = typeof(AnimalBlackboard))]
        public static StateMachineGraph BuildAnimalMachine()
        {
            var builder = new HsmBuilder("AnimalHsm_HSM");
            var graze = builder.State("Graze").Initial()
                .Activity<AnimalBlackboard, AnimalParams, GrazeState>(bb => bb.Params, bb => bb.Graze, Graze);
            var rest = builder.State("Rest")
                .Activity<AnimalBlackboard, AnimalParams>(bb => bb.Params, Rest);
            var alert = builder.State("Alert")
                .Activity<AnimalBlackboard, AnimalParams, FleeState>(bb => bb.Params, bb => bb.MoveOff, Alert);
            var flee = builder.State("Flee")
                .Activity<AnimalBlackboard, AnimalParams, FleeState>(bb => bb.Params, bb => bb.Flee, Flee);
            var recover = builder.State("Recover")
                .Activity<AnimalBlackboard, AnimalParams, RecoverState>(bb => bb.Params, bb => bb.Recover, Recover);
            builder.State("Casualty")
                .Activity<AnimalBlackboard, AnimalParams>(bb => bb.Params, Casualty);
            // ⭐ AB-5 — the guard dog (territorial): bark at a person on its ground, bite one who comes close
            var bark = builder.State("Bark")
                .Activity<AnimalBlackboard, AnimalParams, GuardState>(bb => bb.Params, bb => bb.Guard, Bark);
            var attack = builder.State("Attack")
                .Activity<AnimalBlackboard, AnimalParams, GuardState>(bb => bb.Params, bb => bb.Guard, Attack);

            foreach (var s in new[] { bark, attack })
                s.On(0).GoTo("Casualty").Guard<AnimalBlackboard, AnimalParams>(bb => bb.Params, Down).Polled().Priority(2);
            foreach (var s in new[] { graze, rest })
                s.On(0).GoTo("Bark").Guard<AnimalBlackboard, AnimalParams>(bb => bb.Params, IntruderOnItsGround).Polled().Priority(1);
            bark.On(0).GoTo("Attack").Guard<AnimalBlackboard, AnimalParams>(bb => bb.Params, IntruderClose).Polled();
            bark.On(0).GoTo("Graze").Guard<AnimalBlackboard, AnimalParams>(bb => bb.Params, NoIntruder).Polled();
            attack.On(0).GoTo("Bark").Guard<AnimalBlackboard, AnimalParams>(bb => bb.Params, IntruderBackedOff).Polled();

            foreach (var s in new[] { graze, rest, alert, flee, recover })
                s.On(0).GoTo("Casualty").Guard<AnimalBlackboard, AnimalParams>(bb => bb.Params, Down).Polled().Priority(2);
            foreach (var s in new[] { graze, rest, alert, recover })
                s.On(0).GoTo("Flee").Guard<AnimalBlackboard, AnimalParams>(bb => bb.Params, Panicked).Polled().Priority(1);
            graze.On(0).GoTo("Alert").Guard<AnimalBlackboard, AnimalParams>(bb => bb.Params, Uneasy).Polled();
            graze.On(0).GoTo("Rest").Guard<AnimalBlackboard, AnimalParams>(bb => bb.Params, Night).Polled();
            rest.On(0).GoTo("Alert").Guard<AnimalBlackboard, AnimalParams>(bb => bb.Params, Uneasy).Polled();
            rest.On(0).GoTo("Graze").Guard<AnimalBlackboard, AnimalParams>(bb => bb.Params, Day).Polled();
            alert.On(0).GoTo("Graze").Guard<AnimalBlackboard, AnimalParams>(bb => bb.Params, Calm).Polled();
            flee.On(0).GoTo("Recover").Guard<AnimalBlackboard, AnimalParams, FleeState>(bb => bb.Params, bb => bb.Flee, RanAndBelowPanic).Polled();
            recover.On(0).GoTo("Graze").Guard<AnimalBlackboard, AnimalParams, RecoverState>(bb => bb.Params, bb => bb.Recover, Recovered).Polled();
            return builder.Build();
        }

        // ── states ───────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Grazing: a short walk to a spot round its herd's grazing spot (or its home, or where it was placed), then it stands a while.</summary>
        [SharedAiAction]
        public static NodeStatus Graze(ref AnimalParams p, ref GrazeState ws, Entity self, EntityRepository world)
        {
            SetMode(world, self, AnimalMode.Calm);
            double now = world.SimulationTime;
            var here = Here(world, self);
            if (ws.HasOrigin == 0) { ws.Origin = here; ws.HasOrigin = 1; ws.LastCall = -1; }
            CallAtRest(ref p, ref ws, self, world, now);   // ⭐ AB-8 — a moo, a baa, birdsong, on the closed-form schedule
            if (p.Bird != 0) { SetFlock(world, self, FlockMode.Perched); return NodeStatus.Running; }   // ⭐ AB-5 — a bird sits in its tree
            if (p.Ranges != 0) return Range(ref p, ref ws, self, world, now, here);   // ⭐ CE-4649 part D (AN-21f) — the hunting dog does not graze: it works ahead of its handler
            if (ws.Walking != 0)
            {
                if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
                ws.Walking = 0;
                var rng = SimRng.FromSim((int)self.Index, 0x4624 + ws.Legs, (float)now);
                ws.PauseUntil = (SimInstant)(now + PauseMinSeconds + rng.NextSingle() * (PauseMaxSeconds - PauseMinSeconds));
                return NodeStatus.Running;
            }
            if (now < ws.PauseUntil) return NodeStatus.Running;
            ws.Target = GrazeSpot(world, self, in p, in ws, now);
            ws.Legs++;
            if (LocomotionMoveTo.Issue(world, self, ws.Target, p.Walk, ArrivalMetres, MoveGait.Upright, onSurface: true, source: MoveSource.Wander)) ws.Walking = 1;
            else ws.PauseUntil = (SimInstant)(now + PauseMaxSeconds);
            return NodeStatus.Running;
        }

        /// <summary>The hunting dog sweeps this far ahead of its handler (m) and this far to either side; it chases a flushed animal this long (s) and this far (m), then comes back.</summary>
        public const float RangeAheadMetres = 30f, RangeSideMetres = 25f, ChaseSeconds = 4f, ChaseMetres = 18f, RangeHandlerLeashMetres = 70f;

        /// <summary>
        /// ⭐⭐ <c>CE-4649</c> PART D (AN-21f) — the HUNTING DOG's play over the one brain (beside the guard dog): it RANGES AHEAD of its handler (<see cref="AnimalTasks.Handler"/>, the nearest hunter — the fear system's serial step) in
        /// SWEEPS (a point <see cref="RangeAheadMetres"/> ahead and <see cref="RangeSideMetres"/> to a side, the side alternating); an animal it has FLUSHED (<see cref="AnimalTasks.Quarry"/>, in flight — the dog is a threat in the animal's
        /// own fear) it chases a short way, BARKING, then comes back to its sweeps. ⛔ It never attacks a person: it is not <c>Territorial</c>, has no Bark/Attack state and nothing here targets a person.
        /// </summary>
        private static NodeStatus Range(ref AnimalParams p, ref GrazeState ws, Entity self, EntityRepository world, double now, Vector3 here)
        {
            var tasks = TasksOf(world, self);
            var handler = tasks.Handler;
            if (ws.Phase == 1)
            {
                if (now >= (double)ws.ChaseUntil || LocomotionMoveTo.Status(world, self) != NodeStatus.Running) { ws.Phase = 2; ws.Walking = 0; LocomotionMoveTo.Stop(world, self); }
                else return NodeStatus.Running;
            }
            if (ws.Phase == 0 && !tasks.Quarry.IsNull && Present(world, tasks.Quarry))
            {
                var q = Here(world, tasks.Quarry);
                var d = q - here; d.Z = 0f;
                float len = d.Length();
                if (len > 3f && len <= RangeHandlerLeashMetres)
                {
                    var to = here + d / len * MathF.Min(len, ChaseMetres);
                    Say(world, self, Fdp.Toolkit.Civilians.UtteranceKind.Bark, ChaseSeconds);   // ⭐ it gives voice as it goes
                    if (LocomotionMoveTo.Issue(world, self, to, p.FleeSpeed, ArrivalMetres, MoveGait.Upright, onSurface: true, source: MoveSource.Wander))
                    {
                        ws.Phase = 1; ws.ChaseUntil = (SimInstant)(now + ChaseSeconds); ws.Walking = 1;
                        return NodeStatus.Running;
                    }
                }
            }
            if (ws.Phase == 2) ws.Phase = 0;   // back to its sweeps
            if (ws.Walking != 0)
            {
                if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
                ws.Walking = 0;
                ws.PauseUntil = (SimInstant)(now + 0.5);
                return NodeStatus.Running;
            }
            if (now < ws.PauseUntil) return NodeStatus.Running;
            // the next sweep: ahead of the handler (his heading), alternating sides; no handler ⇒ round where it stands
            Vector3 centre = here, fwd = Vector3.UnitX;
            if (!handler.IsNull && Present(world, handler))
            {
                centre = Here(world, handler);
                var rot = world.GetComponentRO<SimTransform>(handler).Rotation;
                fwd = Vector3.Transform(Vector3.UnitX, (Quaternion)rot); fwd.Z = 0f;
                fwd = fwd.LengthSquared() > 1e-6f ? Vector3.Normalize(fwd) : Vector3.UnitX;
            }
            var left = new Vector3(-fwd.Y, fwd.X, 0f);
            ws.Side = (byte)(ws.Side == 1 ? 2 : 1);
            var rng = SimRng.FromSim((int)self.Index, 0x4630 + ws.Legs, (float)now);
            ws.Legs++;
            var target = centre + fwd * (RangeAheadMetres * (0.7f + 0.6f * rng.NextSingle())) + left * ((ws.Side == 1 ? 1f : -1f) * RangeSideMetres * (0.6f + 0.4f * rng.NextSingle()));
            if (world.IsComponentTypeRegistered<Home>() && world.HasComponent<Home>(self))
            {
                var home = world.GetComponentRO<Home>(self);
                if (home.Radius > 0f) target = KeepHome(world, self, target);
            }
            if (LocomotionMoveTo.Issue(world, self, target, p.Walk * 2f, ArrivalMetres, MoveGait.Upright, onSurface: true, source: MoveSource.Wander)) ws.Walking = 1;
            else ws.PauseUntil = (SimInstant)(now + 2.0);
            return NodeStatus.Running;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.AnimalBehavior.Graze")]
        public static void Deactivate_Graze(ref AnimalParams p, ref GrazeState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            ws.Walking = 0;
            ws.PauseUntil = (SimInstant)0;
        }

        /// <summary>Night: it lies down where it is.</summary>
        [SharedAiAction]
        public static NodeStatus Rest(ref AnimalParams p, Entity self, EntityRepository world)
        {
            SetMode(world, self, AnimalMode.Rest);
            LocomotionMoveTo.Stop(world, self);
            return NodeStatus.Running;
        }

        /// <summary>Uneasy: head up, still. Frightened: it moves off <see cref="MoveOffMetres"/> from the threat at a brisk walk, once.</summary>
        [SharedAiAction]
        public static NodeStatus Alert(ref AnimalParams p, ref FleeState ws, Entity self, EntityRepository world)
        {
            // ⭐ CE-4649 part D (AN-21b) — a FREEZER crouches and stays still while the threat has not come inside its panic zone (the fear system panics it there): lower and harder to see
            if (p.Freezes != 0)
            {
                SetMode(world, self, AnimalMode.Crouch);
                SetStance(world, self, Fdp.Toolkit.Tkb.Domain.StanceId.Crouched);
                LocomotionMoveTo.Stop(world, self);
                return NodeStatus.Running;
            }
            SetMode(world, self, AnimalMode.Alert);
            if (ws.Moving != 0)
            {
                if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
                ws.Moving = 0;
                ws.Arrived = 1;
            }
            if (ws.Arrived != 0) return NodeStatus.Running;
            if (FearOf(world, self).Value < FrightenedAt) { LocomotionMoveTo.Stop(world, self); return NodeStatus.Running; }
            var here = Here(world, self);
            // ⭐ CE-4649 part C ② (AN-20a) — a frightened BIRD takes off: the flight model cruises only while the point is farther than its descent distance, so a 15 m hop never left the ground
            ws.Target = here + Away(world, self, here) * (p.Bird != 0 ? BirdTakeOffMetres : MoveOffMetres);
            if (p.Bird != 0) SetFlock(world, self, FlockMode.Flying);
            if (LocomotionMoveTo.Issue(world, self, ws.Target, p.Walk * 1.6f, ArrivalMetres, MoveGait.Upright, onSurface: true, source: MoveSource.Flee)) ws.Moving = 1;
            return NodeStatus.Running;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.AnimalBehavior.Alert")]
        public static void Deactivate_Alert(ref AnimalParams p, ref FleeState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            if (p.Freezes != 0) SetStance(world, self, Fdp.Toolkit.Tkb.Domain.StanceId.Standing);
            ws = default;
        }

        /// <summary>Panic: it runs its flee distance away from where the fright came from, at its flee speed. A NEW fright while it runs or
        /// stands (the tank keeps coming) sends it off again from the new place.</summary>
        [SharedAiAction]
        public static NodeStatus Flee(ref AnimalParams p, ref FleeState ws, Entity self, EntityRepository world)
        {
            SetMode(world, self, AnimalMode.Flee);
            if (p.Bird != 0) SetFlock(world, self, FlockMode.Flying);   // ⭐ AB-5 — the flock flushes
            double now = world.SimulationTime;
            var fear = FearOf(world, self);
            bool fresh = fear.LastFrightAt > (double)ws.Since && fear.Value >= PanicAt && ws.Arrived != 0;
            if (p.Zigzag != 0) return FleeZigzag(ref p, ref ws, self, world, fresh, now);
            if (ws.Moving != 0)
            {
                if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
                ws.Moving = 0;
                ws.Arrived = 1;
                return NodeStatus.Running;
            }
            if (ws.Arrived != 0 && !fresh) return NodeStatus.Running;
            var here = Here(world, self);
            ws.Target = here + Away(world, self, here) * p.FleeMetres;
            if (TryHome(world, self, out var pen) && pen.Enclosed != 0) ws.Target = KeepHome(world, self, ws.Target);   // ⭐ AB-9 — a pen holds it
            if ((double)ws.Since == 0) Say(world, self, Fdp.Toolkit.Civilians.UtteranceKind.AnimalAlarm, 2f);   // ⭐ AB-8 — the alarm as it bolts
            ws.Since = (SimInstant)now;
            ws.Arrived = 0;
            if (LocomotionMoveTo.Issue(world, self, ws.Target, p.FleeSpeed, ArrivalMetres * 2f, MoveGait.Upright, onSurface: true, source: MoveSource.Flee)) ws.Moving = 1;
            else ws.Arrived = 1;   // nothing to run with
            return NodeStatus.Running;
        }

        /// <summary>⭐ CE-4649 part D (AN-21c) — the zigzag flee: a chain of short legs (0.6–1.2 s each), every one turned 30–50° off the run's line, the side alternating with a seeded chance of a double jink; the
        /// line BENDS toward the nearest wood within reach and the run ends INSIDE it (after two legs), else when it has run its distance. Per-entity seeded, no allocation.</summary>
        private static NodeStatus FleeZigzag(ref AnimalParams p, ref FleeState ws, Entity self, EntityRepository world, bool fresh, double now)
        {
            SetStance(world, self, Fdp.Toolkit.Tkb.Domain.StanceId.Standing);
            if (ws.Moving != 0)
            {
                if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
                ws.Moving = 0;
            }
            var here = Here(world, self);
            var woods = Fdp.Toolkit.World.WorldQuery.Of(world);
            if (ws.Legs > 0 && ws.Arrived == 0 && (ws.Run >= p.FleeMetres || (ws.Legs >= 2 && woods != null && woods.WoodDensityAt(here.X, here.Y) > 0f)))
            {
                ws.Arrived = 1;   // inside the wood, or it has run its distance
                return NodeStatus.Running;
            }
            if (ws.Arrived != 0 && !fresh) return NodeStatus.Running;
            if (ws.Arrived != 0 || ws.Legs == 0)   // a new run (the first, or a fresh fright after it stopped)
            {
                if ((double)ws.Since == 0) Say(world, self, Fdp.Toolkit.Civilians.UtteranceKind.AnimalAlarm, 2f);
                ws.Since = (SimInstant)now; ws.Arrived = 0; ws.Legs = 0; ws.Run = 0f;
                ws.Base = Away(world, self, here);
            }
            ws.Base = BendToWood(woods, here, ws.Base, p.FleeMetres);
            ws.Target = ZigzagTarget(here, ws.Base, (int)self.Index, ws.Legs, now, p.FleeSpeed, ref ws.Side, out float length);
            ws.Legs++; ws.Run += length;
            if (LocomotionMoveTo.Issue(world, self, ws.Target, p.FleeSpeed, ArrivalMetres, MoveGait.Upright, onSurface: true, source: MoveSource.Flee)) ws.Moving = 1;
            else ws.Arrived = 1;
            return NodeStatus.Running;
        }

        /// <summary>The jink angle range (rad) and the leg duration range (s) of the zigzag, and the chance that the next jink keeps the SAME side (a double jink).</summary>
        public const float JinkMinRad = 30f * MathF.PI / 180f, JinkMaxRad = 50f * MathF.PI / 180f, LegMinSeconds = 0.6f, LegMaxSeconds = 1.2f, DoubleJinkChance = 0.25f;

        /// <summary>⭐ The ONE leg of the zigzag: the point <paramref name="length"/> m on from <paramref name="here"/>, turned off <paramref name="baseDir"/> by a seeded 30–50° to <paramref name="side"/> (±1, alternating; a seeded chance keeps it).
        /// Pure and allocation-free — a rail calls it directly.</summary>
        public static Vector3 ZigzagTarget(Vector3 here, Vector3 baseDir, int entityIndex, int leg, double now, float speed, ref byte side, out float length)
        {
            var rng = SimRng.FromSim(entityIndex, 0x2163 + leg, (float)now);
            float ang = JinkMinRad + (JinkMaxRad - JinkMinRad) * rng.NextSingle();
            float dur = LegMinSeconds + (LegMaxSeconds - LegMinSeconds) * rng.NextSingle();
            bool keep = leg > 0 && rng.NextSingle() < DoubleJinkChance;
            side = (byte)(side == 0 ? (rng.NextSingle() < 0.5f ? 1 : 2) : keep ? side : (side == 1 ? 2 : 1));   // 1 = left, 2 = right
            float a = side == 1 ? ang : -ang;
            float c = MathF.Cos(a), s = MathF.Sin(a);
            var d = new Vector3(baseDir.X * c - baseDir.Y * s, baseDir.X * s + baseDir.Y * c, 0f);
            length = speed * dur;
            return here + d * length;
        }

        /// <summary>⭐ The run's line bent toward the NEAREST wood within <paramref name="reach"/> (16 directions, stepping out in 12 m): 0.3 the old line + 0.7 toward the wood; unchanged when none is in reach or it is in the wood already.</summary>
        public static Vector3 BendToWood(Fdp.Toolkit.World.IWorldQuery? woods, Vector3 here, Vector3 line, float reach)
        {
            if (woods == null || woods.WoodDensityAt(here.X, here.Y) > 0f) return line;
            for (float r = 12f; r <= reach; r += 12f)
            {
                float bestScore = -2f; Vector3 best = default; bool found = false;
                for (int i = 0; i < 16; i++)
                {
                    float a = i * (MathF.Tau / 16f);
                    var d = new Vector3(MathF.Cos(a), MathF.Sin(a), 0f);
                    if (woods.WoodDensityAt(here.X + d.X * r, here.Y + d.Y * r) <= 0f) continue;
                    float score = Vector3.Dot(d, line);   // among the wood cells at this reach, the one most along the way it was running
                    if (score > bestScore) { bestScore = score; best = d; found = true; }
                }
                if (!found) continue;
                var bent = line * 0.3f + best * 0.7f;
                float l = bent.Length();
                return l > 1e-4f ? bent / l : best;
            }
            return line;
        }

        private static void SetStance(EntityRepository world, Entity self, Fdp.Toolkit.Tkb.Domain.StanceId stance)
        {
            if (!world.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>() || !world.HasComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(self)) return;
            if (world.GetComponentRO<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(self).TargetStance == stance) return;
            ref var si = ref world.GetComponentRW<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(self);
            si.TargetStance = stance; si.Version++;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.AnimalBehavior.Flee")]
        public static void Deactivate_Flee(ref AnimalParams p, ref FleeState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            ws = default;
        }

        /// <summary>After the run: it stands looking back a share of its recovery time; the machine grazes again once it is calm.</summary>
        [SharedAiAction]
        public static NodeStatus Recover(ref AnimalParams p, ref RecoverState ws, Entity self, EntityRepository world)
        {
            SetMode(world, self, AnimalMode.Recover);
            if (p.Bird != 0) SetFlock(world, self, FlockMode.Landed);   // ⭐ AB-5 — it comes down where it flew to
            if (ws.Started == 0)
            {
                ws.Started = 1;
                ws.Until = (SimInstant)(world.SimulationTime + p.Recovery * LookBackShare);
                LocomotionMoveTo.Stop(world, self);
            }
            return NodeStatus.Running;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.AnimalBehavior.Recover")]
        public static void Deactivate_Recover(ref AnimalParams p, ref RecoverState ws, Entity self, EntityRepository world) => ws = default;

        /// <summary>Down (incapacitated or dead): it lies where it fell.</summary>
        [SharedAiAction]
        public static NodeStatus Casualty(ref AnimalParams p, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            return NodeStatus.Running;
        }

        /// <summary>A guard dog stands this far from the man it barks at (m).</summary>
        public const float BarkStandOffMetres = 4f;
        /// <summary>It goes for a man this close (m) who is on its ground.</summary>
        public const float AttackMetres = 6f;
        /// <summary>A bite: little damage, a short stagger — never the long knock-down of a blow (AR-6).</summary>
        public const float BiteDamage = 8f, BiteKnockdownSeconds = 0.3f, BiteCooldownSeconds = 1.5f;
        /// <summary>It re-aims its stand-off this often while barking (s).</summary>
        public const float BarkAimSeconds = 1f;

        /// <summary>⭐ AB-5 — BARK: it calls (the bark — <see cref="AnimalFlags.Calling"/>) and stands between the man and its ground, kept inside
        /// its home and its chain.</summary>
        [SharedAiAction]
        public static NodeStatus Bark(ref AnimalParams p, ref GuardState ws, Entity self, EntityRepository world)
        {
            SetMode(world, self, AnimalMode.Guard);
            SetFlags(world, self, AnimalFlags.Calling, AnimalFlags.Dangerous);
            double now = world.SimulationTime;
            var who = TasksOf(world, self).Intruder;
            if (now < (double)ws.NextAimAt || !Present(world, who)) return NodeStatus.Running;
            ws.NextAimAt = (SimInstant)(now + BarkAimSeconds);
            Say(world, self, Fdp.Toolkit.Civilians.UtteranceKind.Bark, BarkAimSeconds);   // ⭐ AB-8 — the bark, every re-aim
            var here = Here(world, self);
            var there = Here(world, who);
            var off = here - there; off.Z = 0f;
            var stand = there + (off.LengthSquared() > 1e-4f ? Vector3.Normalize(off) : Vector3.UnitX) * BarkStandOffMetres;
            stand = KeepHome(world, self, stand);
            if (Vector2.Distance(new Vector2(stand.X, stand.Y), new Vector2(here.X, here.Y)) > ArrivalMetres)
                LocomotionMoveTo.Issue(world, self, stand, p.Walk * 2.5f, ArrivalMetres, MoveGait.Upright, onSurface: true, source: MoveSource.Attack);
            else LocomotionMoveTo.Stop(world, self);
            return NodeStatus.Running;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.AnimalBehavior.Bark")]
        public static void Deactivate_Bark(ref AnimalParams p, ref GuardState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            SetFlags(world, self, 0, AnimalFlags.Calling | AnimalFlags.Dangerous);
            ws = default;
        }

        /// <summary>⭐ AB-5 — ATTACK: it runs at the man and BITES when in reach (<see cref="Fdp.Toolkit.Combat.Melee"/>, a bite: little damage, a
        /// short stagger), marked DANGEROUS while it does — self-defence covers firing at it (AR-6).</summary>
        [SharedAiAction]
        public static NodeStatus Attack(ref AnimalParams p, ref GuardState ws, Entity self, EntityRepository world)
        {
            SetMode(world, self, AnimalMode.Guard);
            SetFlags(world, self, AnimalFlags.Calling | AnimalFlags.Dangerous, 0);
            double now = world.SimulationTime;
            var who = TasksOf(world, self).Intruder;
            if (!Present(world, who)) return NodeStatus.Running;
            if (Fdp.Toolkit.Combat.Melee.InReach(world, self, who))
            {
                LocomotionMoveTo.Stop(world, self);
                if (now >= (double)ws.NextBiteAt
                    && Fdp.Toolkit.Combat.Melee.Strike(world, self, who, BiteDamage, BiteKnockdownSeconds, Fdp.Toolkit.Combat.MeleeWeapon.MeleeBite))
                    ws.NextBiteAt = (SimInstant)(now + BiteCooldownSeconds);
                return NodeStatus.Running;
            }
            if (now >= (double)ws.NextAimAt)
            {
                if (Fdp.Toolkit.Civilians.Utterances.Of(world, self, now, out _) != Fdp.Toolkit.Civilians.UtteranceKind.Bark)
                    Say(world, self, Fdp.Toolkit.Civilians.UtteranceKind.Bark, BarkAimSeconds);
                ws.NextAimAt = (SimInstant)(now + 0.25);
                LocomotionMoveTo.Issue(world, self, KeepHome(world, self, Here(world, who)), p.FleeSpeed, 0.5f, MoveGait.Upright, onSurface: true, source: MoveSource.Attack);
            }
            return NodeStatus.Running;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.AnimalBehavior.Attack")]
        public static void Deactivate_Attack(ref AnimalParams p, ref GuardState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            SetFlags(world, self, 0, AnimalFlags.Dangerous);
            ws = default;
        }

        [SharedAiCondition]
        public static bool IntruderOnItsGround(ref AnimalParams p, Entity self, EntityRepository world)
            => p.Territorial != 0 && Present(world, TasksOf(world, self).Intruder);

        [SharedAiCondition]
        public static bool NoIntruder(ref AnimalParams p, Entity self, EntityRepository world) => !Present(world, TasksOf(world, self).Intruder);

        /// <summary>The man is within <see cref="AttackMetres"/> AND on its ground (inside its home, when it has one).</summary>
        [SharedAiCondition]
        public static bool IntruderClose(ref AnimalParams p, Entity self, EntityRepository world)
        {
            var who = TasksOf(world, self).Intruder;
            if (!Present(world, who)) return false;
            var there = Here(world, who);
            if (Flat(there - Here(world, self)) > AttackMetres) return false;
            return !TryHome(world, self, out var home) || Flat(there - home.Centre) <= home.Radius;
        }

        /// <summary>The man is gone, off its ground, or twice the attack distance away.</summary>
        [SharedAiCondition]
        public static bool IntruderBackedOff(ref AnimalParams p, Entity self, EntityRepository world)
        {
            var who = TasksOf(world, self).Intruder;
            if (!Present(world, who)) return true;
            var there = Here(world, who);
            if (Flat(there - Here(world, self)) > AttackMetres * 2f) return true;
            return TryHome(world, self, out var home) && Flat(there - home.Centre) > home.Radius;
        }

        // ── guards ───────────────────────────────────────────────────────────────────────────────────────────────────

        [SharedAiCondition]
        public static bool Down(ref AnimalParams p, Entity self, EntityRepository world) => !Fdp.Toolkit.Combat.Injury.IsInTheFight(world, self);

        [SharedAiCondition]
        public static bool Panicked(ref AnimalParams p, Entity self, EntityRepository world) => p.Fearless == 0 && FearOf(world, self).Value >= PanicAt;

        [SharedAiCondition]
        public static bool Uneasy(ref AnimalParams p, Entity self, EntityRepository world)
        {
            float f = FearOf(world, self).Value;
            return p.Fearless == 0 && f >= UneasyAt && f < PanicAt;
        }

        [SharedAiCondition]
        public static bool Calm(ref AnimalParams p, Entity self, EntityRepository world) => FearOf(world, self).Value < CalmBelow;

        /// <summary>Its herd lies down for the night (the <see cref="HerdSystem"/>'s verdict; a lone animal has no night yet).</summary>
        [SharedAiCondition]
        public static bool Night(ref AnimalParams p, Entity self, EntityRepository world)
            => HerdOf(world, self, out var herd, out _) && herd.Mode == AnimalMode.Rest;

        [SharedAiCondition]
        public static bool Day(ref AnimalParams p, Entity self, EntityRepository world) => !Night(ref p, self, world);

        [SharedAiCondition]
        public static bool RanAndBelowPanic(ref AnimalParams p, ref FleeState ws, Entity self, EntityRepository world)
            => ws.Arrived != 0 && FearOf(world, self).Value < PanicAt;

        [SharedAiCondition]
        public static bool Recovered(ref AnimalParams p, ref RecoverState ws, Entity self, EntityRepository world)
            => ws.Started != 0 && world.SimulationTime >= (double)ws.Until && FearOf(world, self).Value < UneasyAt;

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

        private static Fear FearOf(EntityRepository world, Entity self)
            => world.IsComponentTypeRegistered<Fear>() && world.HasComponent<Fear>(self) ? world.GetComponentRO<Fear>(self) : default;

        /// <summary>⭐ <c>CE-4628</c> — its animal-only tasks (intruder, handler, quarry): the part of its fear state that is not shared with a civilian.</summary>
        private static AnimalTasks TasksOf(EntityRepository world, Entity self)
            => world.IsComponentTypeRegistered<AnimalTasks>() && world.HasComponent<AnimalTasks>(self) ? world.GetComponentRO<AnimalTasks>(self) : default;

        private static Vector3 Here(EntityRepository world, Entity self)
            => world.HasComponent<SimTransform>(self) ? (Vector3)world.GetComponentRO<SimTransform>(self).Position : Vector3.Zero;

        /// <summary>Its own mode, written only when it changes (no chunk-version churn, so the wire sends one sample a change).</summary>
        private static void SetMode(EntityRepository world, Entity self, AnimalMode mode)
        {
            if (!world.IsComponentTypeRegistered<AnimalState>() || !world.HasComponent<AnimalState>(self)) return;
            if (world.GetComponentRO<AnimalState>(self).Mode != mode) world.GetComponentRW<AnimalState>(self).Mode = mode;
        }

        private static float Flat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

        /// <summary>⭐ AB-8 — it says <paramref name="kind"/> (the one voice door; its <c>Utterance</c> exists from spawn, so this only writes).</summary>
        private static void Say(EntityRepository world, Entity self, Fdp.Toolkit.Civilians.UtteranceKind kind, float seconds)
        {
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Civilians.Utterance>() || !world.HasComponent<Fdp.Toolkit.Civilians.Utterance>(self)) return;
            Fdp.Toolkit.Civilians.Utterances.Say(world, self, kind, Entity.Null, world.SimulationTime, seconds);
        }

        /// <summary>⭐ AB-8 — a call at rest when its closed-form schedule says so (once per call number; a herd's members share the herd's rate).</summary>
        private static void CallAtRest(ref AnimalParams p, ref GrazeState ws, Entity self, EntityRepository world, double now)
        {
            if (p.CallInterval <= 0f) return;
            int herd = HerdOf(world, self, out _, out var g) ? Math.Max(1, g.Count) : 1;
            long k = AnimalCalls.CallDue((uint)self.Index, now, p.CallInterval, herd);
            if (k < 0 || k == ws.LastCall) return;
            ws.LastCall = k;
            Say(world, self, Fdp.Toolkit.Civilians.UtteranceKind.AnimalCall, AnimalCalls.CallSeconds);
        }

        private static bool Present(EntityRepository world, Entity who) => !who.IsNull && Fdp.Toolkit.Combat.Injury.IsInTheFight(world, who);

        private static bool TryHome(EntityRepository world, Entity self, out Home home)
        {
            home = default;
            if (!world.IsComponentTypeRegistered<Home>() || !world.HasComponent<Home>(self)) return false;
            home = world.GetComponentRO<Home>(self);
            return home.Radius > 0f || home.TetherMetres > 0f;
        }

        /// <summary>A point kept inside its home and on its chain (the tether, AB-5).</summary>
        private static Vector3 KeepHome(EntityRepository world, Entity self, Vector3 p)
        {
            if (!TryHome(world, self, out var home)) return p;
            float limit = home.TetherMetres > 0f ? (home.Radius > 0f ? MathF.Min(home.TetherMetres, home.Radius) : home.TetherMetres) : home.Radius;
            var d = new Vector2(p.X - home.Centre.X, p.Y - home.Centre.Y);
            float len = d.Length();
            if (len <= limit || len < 1e-4f) return p;
            d *= limit / len;
            return new Vector3(home.Centre.X + d.X, home.Centre.Y + d.Y, p.Z);
        }

        /// <summary>Sets <paramref name="on"/> and clears <paramref name="off"/> on its own flags, writing only on a change.</summary>
        private static void SetFlags(EntityRepository world, Entity self, AnimalFlags on, AnimalFlags off)
        {
            if (!world.IsComponentTypeRegistered<AnimalState>() || !world.HasComponent<AnimalState>(self)) return;
            var f = world.GetComponentRO<AnimalState>(self).Flags;
            var n = (f | on) & ~off;
            if (n != f) world.GetComponentRW<AnimalState>(self).Flags = n;
        }

        /// <summary>⭐ AB-5 — a flock's (or a lone bird's) flying / landed / perched, on its own <see cref="FlockState"/>, on a change.</summary>
        private static void SetFlock(EntityRepository world, Entity self, FlockMode mode)
        {
            // ⭐ CE-11008 (RR-8, R-11002; cross-lane edit by ui — the brain is the ONE writer of AnimalState) — a LONE bird has no FlockState, so its flight is
            //   said on its own flags too: every node, a recording and an external renderer read "flying" from state
            if (mode == FlockMode.Flying) SetFlags(world, self, AnimalFlags.InFlight, AnimalFlags.None);
            else SetFlags(world, self, AnimalFlags.None, AnimalFlags.InFlight);
            if (!world.IsComponentTypeRegistered<FlockState>() || !world.HasComponent<FlockState>(self)) return;
            if (world.GetComponentRO<FlockState>(self).Mode != mode) world.GetComponentRW<FlockState>(self).Mode = mode;
        }

        /// <summary>⭐ <c>CE-4628</c> — the herd it belongs to: the herd entity's <see cref="HerdState"/> and its <see cref="Group"/> header (the count, the centre).</summary>
        private static bool HerdOf(EntityRepository world, Entity self, out HerdState herd, out Group group)
        {
            herd = default; group = default;
            if (!world.IsComponentTypeRegistered<GroupMember>() || !world.HasComponent<GroupMember>(self)) return false;
            var h = world.GetComponentRO<GroupMember>(self).Group;
            if (h.IsNull || !world.IsAlive(h) || !world.HasComponent<HerdState>(h) || !world.HasComponent<Group>(h)) return false;
            herd = world.GetComponentRO<HerdState>(h);
            group = world.GetComponentRO<Group>(h);
            return true;
        }

        /// <summary>The flat unit direction AWAY from where its fright came from; none known ⇒ a direction of its own (by its index).</summary>
        private static Vector3 Away(EntityRepository world, Entity self, Vector3 here)
        {
            var f = FearOf(world, self);
            Vector2 d = f.HasFrightFrom != 0 ? new Vector2(here.X - f.FrightFrom.X, here.Y - f.FrightFrom.Y) : Vector2.Zero;
            if (d.LengthSquared() < 1e-4f)
            {
                float a = (self.Index * 2.399963f) % MathF.Tau;   // the golden angle: neighbours scatter
                d = new Vector2(MathF.Cos(a), MathF.Sin(a));
            }
            d = Vector2.Normalize(d);
            return new Vector3(d.X, d.Y, 0f);
        }

        private static Vector3 GrazeSpot(EntityRepository world, Entity self, in AnimalParams p, in GrazeState ws, double now)
        {
            var rng = SimRng.FromSim((int)self.Index, 0x4625 + ws.Legs, (float)now);
            float ang = rng.NextSingle() * MathF.Tau;
            float u = MathF.Sqrt(rng.NextSingle());
            if (HerdOf(world, self, out var herd, out _) && herd.NextGrazing != Vector3.Zero)
            {
                float spread = TightSpreadMetres + LooseSpreadMetres * (1f - Math.Clamp(p.Cohesion, 0f, 1f));
                // ⭐ CE-1958 — a member's spot round the herd's is kept in its HOME: the spread (up to 11 m for a pig) reached past a pen's wall, and the
                //   pig walked into the wall for good
                return KeepHome(world, self, herd.NextGrazing + new Vector3(MathF.Cos(ang), MathF.Sin(ang), 0f) * (u * spread));
            }
            Vector3 centre = ws.Origin;
            float radius = LoneGrazeMetres;
            if (world.IsComponentTypeRegistered<Home>() && world.HasComponent<Home>(self))
            {
                var home = world.GetComponentRO<Home>(self);
                if (home.Radius > 0f) { centre = home.Centre; radius = home.Radius; }
            }
            var spot = centre + new Vector3(MathF.Cos(ang), MathF.Sin(ang), 0f) * (u * radius * 0.85f);
            if (p.Zigzag == 0) return spot;
            // ⭐ CE-4649 part D (AN-21a) — a wood dweller (the rabbit) nibbles at a WOOD EDGE or in the wood: a spot in or beside a wood cell (up to four tries, then the last)
            var woods = Fdp.Toolkit.World.WorldQuery.Of(world);
            for (int i = 0; i < 4 && woods != null && !NearWood(woods, spot); i++)
            {
                ang = rng.NextSingle() * MathF.Tau; u = MathF.Sqrt(rng.NextSingle());
                spot = centre + new Vector3(MathF.Cos(ang), MathF.Sin(ang), 0f) * (u * radius * 0.85f);
            }
            return spot;
        }

        /// <summary>A place in a wood cell or beside one (a wood's edge): the wood grid's 25 m cell, tried at the spot and one cell either way.</summary>
        public static bool NearWood(Fdp.Toolkit.World.IWorldQuery woods, Vector3 at)
            => woods.WoodDensityAt(at.X, at.Y) > 0f || woods.WoodDensityAt(at.X + 25f, at.Y) > 0f || woods.WoodDensityAt(at.X - 25f, at.Y) > 0f
               || woods.WoodDensityAt(at.X, at.Y + 25f) > 0f || woods.WoodDensityAt(at.X, at.Y - 25f) > 0f;
    }
}
