using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Diagnostics;
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
            public float Walk, FleeSpeed, FleeMetres, RecoverySeconds, Cohesion;
            public byte Fearless;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GrazeState
        {
            public byte Walking, HasOrigin;
            public int Legs;
            public SimInstant PauseUntil;
            public Vector3 Target, Origin;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FleeState
        {
            public byte Moving, Arrived;
            public SimInstant Since;
            public Vector3 Target;
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
                    RecoverySeconds = t is { RecoverySeconds: > 0f } ? t.RecoverySeconds : 30f,
                    Cohesion = t?.HerdCohesion ?? 0f,
                    Fearless = (byte)(t is { Fearless: true } ? 1 : 0),
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
            if (ws.HasOrigin == 0) { ws.Origin = here; ws.HasOrigin = 1; }
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
            ws.Target = here + Away(world, self, here) * MoveOffMetres;
            if (LocomotionMoveTo.Issue(world, self, ws.Target, p.Walk * 1.6f, ArrivalMetres, MoveGait.Upright, onSurface: true, source: MoveSource.Flee)) ws.Moving = 1;
            return NodeStatus.Running;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.AnimalBehavior.Alert")]
        public static void Deactivate_Alert(ref AnimalParams p, ref FleeState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            ws = default;
        }

        /// <summary>Panic: it runs its flee distance away from where the fright came from, at its flee speed. A NEW fright while it runs or
        /// stands (the tank keeps coming) sends it off again from the new place.</summary>
        [SharedAiAction]
        public static NodeStatus Flee(ref AnimalParams p, ref FleeState ws, Entity self, EntityRepository world)
        {
            SetMode(world, self, AnimalMode.Flee);
            double now = world.SimulationTime;
            var fear = FearOf(world, self);
            bool fresh = fear.LastFrightAt > (double)ws.Since && fear.Value >= PanicAt && ws.Arrived != 0;
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
            ws.Since = (SimInstant)now;
            ws.Arrived = 0;
            if (LocomotionMoveTo.Issue(world, self, ws.Target, p.FleeSpeed, ArrivalMetres * 2f, MoveGait.Upright, onSurface: true, source: MoveSource.Flee)) ws.Moving = 1;
            else ws.Arrived = 1;   // nothing to run with
            return NodeStatus.Running;
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
            if (ws.Started == 0)
            {
                ws.Started = 1;
                ws.Until = (SimInstant)(world.SimulationTime + p.RecoverySeconds * LookBackShare);
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
            => HerdOf(world, self, out var herd) && herd.Mode == AnimalMode.Rest;

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

        private static Vector3 Here(EntityRepository world, Entity self)
            => world.HasComponent<SimTransform>(self) ? (Vector3)world.GetComponentRO<SimTransform>(self).Position : Vector3.Zero;

        /// <summary>Its own mode, written only when it changes (no chunk-version churn, so the wire sends one sample a change).</summary>
        private static void SetMode(EntityRepository world, Entity self, AnimalMode mode)
        {
            if (!world.IsComponentTypeRegistered<AnimalState>() || !world.HasComponent<AnimalState>(self)) return;
            if (world.GetComponentRO<AnimalState>(self).Mode != mode) world.GetComponentRW<AnimalState>(self).Mode = mode;
        }

        private static bool HerdOf(EntityRepository world, Entity self, out HerdState herd)
        {
            herd = default;
            if (!world.IsComponentTypeRegistered<HerdMember>() || !world.HasComponent<HerdMember>(self)) return false;
            var h = world.GetComponentRO<HerdMember>(self).Herd;
            if (h.IsNull || !world.IsAlive(h) || !world.HasComponent<HerdState>(h)) return false;
            herd = world.GetComponentRO<HerdState>(h);
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
            if (HerdOf(world, self, out var herd) && herd.NextGrazing != Vector3.Zero)
            {
                float spread = TightSpreadMetres + LooseSpreadMetres * (1f - Math.Clamp(p.Cohesion, 0f, 1f));
                return herd.NextGrazing + new Vector3(MathF.Cos(ang), MathF.Sin(ang), 0f) * (u * spread);
            }
            Vector3 centre = ws.Origin;
            float radius = LoneGrazeMetres;
            if (world.IsComponentTypeRegistered<AnimalHome>() && world.HasComponent<AnimalHome>(self))
            {
                var home = world.GetComponentRO<AnimalHome>(self);
                if (home.Radius > 0f) { centre = home.Centre; radius = home.Radius; }
            }
            return centre + new Vector3(MathF.Cos(ang), MathF.Sin(ang), 0f) * (u * radius * 0.85f);
        }
    }
}
