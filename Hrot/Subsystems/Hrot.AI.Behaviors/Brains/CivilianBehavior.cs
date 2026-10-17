using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Determinism;
using Fdp.Toolkit.Tkb.Domain;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Attributes;
using Hrot.Map.Definitions.Behavior;
using Hrot.MuscleCharacter.Animation.Stance;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2235</c> (civilians CIV-1, CV-3, R-3013; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §2.2, §5d) — <c>CivilianHsm</c>, a
    /// civilian's ONE behaviour. Slice 1: <b>Routine</b> (he wanders about his home, lingering), <b>Alarmed</b> (what he heard frightened
    /// him: he stops and crouches until his fear fades) and <b>Casualty</b> (wounded: he lies where he fell). His fear is
    /// <see cref="CivilianState.Fear"/>, raised by <see cref="CivilianFearSystem"/> from what he hears; this machine only reads it. Panic,
    /// shelter, protest and riot are states of this same machine in the later slices (CE-2236 … CE-2238).
    /// </summary>
    public static class CivilianBehavior
    {
        public const float DefaultWanderRadius = 12f;
        public const float DefaultWalkSpeed = 1.4f;
        public const float DefaultPauseMinSeconds = 3f;
        public const float DefaultPauseMaxSeconds = 10f;
        public const float DefaultAlarmAt = 0.3f;
        public const float DefaultCalmBelow = 0.1f;
        public const float CrouchSeconds = 0.4f;
        public const float FallSeconds = 0.8f;

        [StructLayout(LayoutKind.Sequential)]
        public struct CivilianParams
        {
            public float WanderRadius, WalkSpeed, PauseMinSeconds, PauseMaxSeconds;
            public float AlarmAt, CalmBelow;
        }

        /// <summary>The routine's working memory: walking to <see cref="Target"/>, or lingering until <see cref="PauseUntil"/>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RoutineState
        {
            public byte Walking;
            public int Legs;
            public double PauseUntil;
            public Vector3 Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CivilianBlackboard
        {
            public CivilianParams Params;
            public RoutineState Routine;
        }

        public static CivilianParams Defaults() => new CivilianParams
        {
            WanderRadius = DefaultWanderRadius, WalkSpeed = DefaultWalkSpeed,
            PauseMinSeconds = DefaultPauseMinSeconds, PauseMaxSeconds = DefaultPauseMaxSeconds,
            AlarmAt = DefaultAlarmAt, CalmBelow = DefaultCalmBelow,
        };

        /// <summary>⭐ The defaults, then the authored values — a zero field keeps its default (a TKB-started civilian authors nothing).</summary>
        [BehaviorResolver(BehaviorNames.CivilianHsm)]
        public static void Resolve(in CivilianParamsJsonDto authored, ref CivilianBlackboard block, EntityRepository world, Entity self)
        {
            var p = Defaults();
            if (authored.WanderRadius > 0f) p.WanderRadius = authored.WanderRadius;
            if (authored.WalkSpeed > 0f) p.WalkSpeed = authored.WalkSpeed;
            if (authored.PauseMinSeconds > 0f) p.PauseMinSeconds = authored.PauseMinSeconds;
            if (authored.PauseMaxSeconds > 0f) p.PauseMaxSeconds = authored.PauseMaxSeconds;
            if (p.PauseMaxSeconds < p.PauseMinSeconds) p.PauseMaxSeconds = p.PauseMinSeconds;
            if (authored.AlarmAt > 0f) p.AlarmAt = authored.AlarmAt;
            if (authored.CalmBelow > 0f) p.CalmBelow = authored.CalmBelow;
            block = new CivilianBlackboard { Params = p };
        }

        /// <summary>📄 §5d: Routine ⇄ Alarmed on his fear; either ⇒ Casualty once he is wounded (first, so a frightened wounded man lies
        /// down rather than crouches). Casualty has no way out in slice 1.</summary>
        [Behavior(BehaviorNames.CivilianHsm, Params = typeof(CivilianParamsJsonDto), Categories = BehaviorCategory.Civilian)]
        [HsmDefinition(BehaviorNames.CivilianHsm, Curated = true, Group = "Library/Civilian", ParamsType = typeof(CivilianBlackboard))]
        public static StateMachineGraph BuildCivilianMachine()
        {
            var builder = new HsmBuilder("CivilianHsm_HSM");
            var routine = builder.State("Routine").Initial()
                .Activity<CivilianBlackboard, CivilianParams, RoutineState>(bb => bb.Params, bb => bb.Routine, Routine);
            var alarmed = builder.State("Alarmed")
                .Activity<CivilianBlackboard, CivilianParams>(bb => bb.Params, Alarmed);
            builder.State("Casualty")
                .Activity<CivilianBlackboard, CivilianParams>(bb => bb.Params, Casualty);

            routine.On(0).GoTo("Casualty").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Wounded).Polled().Priority(1);
            routine.On(0).GoTo("Alarmed").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Frightened).Polled();
            alarmed.On(0).GoTo("Casualty").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Wounded).Polled().Priority(1);
            alarmed.On(0).GoTo("Routine").Guard<CivilianBlackboard, CivilianParams>(bb => bb.Params, Calm).Polled();
            return builder.Build();
        }

        // ── states ───────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>He wanders about his home: walks to a random spot within <see cref="CivilianParams.WanderRadius"/>, lingers a random
        /// while, walks on. A man with no locomotion just stands. Running.</summary>
        [SharedAiAction]
        public static NodeStatus Routine(ref CivilianParams p, ref RoutineState ws, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Routine);
            double now = world.SimulationTime;
            if (ws.Walking != 0)
            {
                if (LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
                ws.Walking = 0;
                var rng = SimRng.FromSim((int)self.Index, 0x2235 + ws.Legs, (float)now);
                ws.PauseUntil = now + p.PauseMinSeconds + rng.NextSingle() * (p.PauseMaxSeconds - p.PauseMinSeconds);
                return NodeStatus.Running;
            }
            if (now < ws.PauseUntil) return NodeStatus.Running;
            if (!world.HasComponent<CivilianState>(self)) return NodeStatus.Running;
            ref readonly var mind = ref world.GetComponentRO<CivilianState>(self);
            if (mind.HasHome == 0) return NodeStatus.Running;
            ws.Target = WanderPoint(mind.Home, p.WanderRadius, (int)self.Index, ws.Legs, now);
            ws.Legs++;
            if (LocomotionMoveTo.Issue(world, self, ws.Target, p.WalkSpeed, 1f, MoveGait.Upright, onSurface: true)) ws.Walking = 1;
            else ws.PauseUntil = now + p.PauseMaxSeconds;   // nothing to walk with: look again later
            return NodeStatus.Running;
        }

        /// <summary>Leaving the routine: he stops where he is; the next routine starts with a walk.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.CivilianBehavior.Routine")]
        public static void Deactivate_Routine(ref CivilianParams p, ref RoutineState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            ws.Walking = 0;
            ws.PauseUntil = 0;
        }

        /// <summary>Alarmed: he stops and crouches where he is. Running until his fear fades (the machine's guard).</summary>
        [SharedAiAction]
        public static NodeStatus Alarmed(ref CivilianParams p, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Alarmed);
            LocomotionMoveTo.Stop(world, self);
            StanceRequest.Set(world, self, StanceId.Crouched, CrouchSeconds);
            return NodeStatus.Running;
        }

        /// <summary>Leaving the alarm: he stands up.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.CivilianBehavior.Alarmed")]
        public static void Deactivate_Alarmed(ref CivilianParams p, Entity self, EntityRepository world)
            => StanceRequest.Set(world, self, StanceId.Standing, CrouchSeconds);

        /// <summary>A casualty: he lies where he fell and does nothing else. Running.</summary>
        [SharedAiAction]
        public static NodeStatus Casualty(ref CivilianParams p, Entity self, EntityRepository world)
        {
            CivilianMind.SetMode(world, self, CivilianMode.Casualty);
            LocomotionMoveTo.Stop(world, self);
            StanceRequest.Set(world, self, StanceId.Prone, FallSeconds);
            return NodeStatus.Running;
        }

        // ── guards ───────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>His fear has reached <see cref="CivilianParams.AlarmAt"/>.</summary>
        [SharedAiCondition]
        public static bool Frightened(ref CivilianParams p, Entity self, EntityRepository world)
            => CivilianMind.FearOf(world, self) >= p.AlarmAt;

        /// <summary>His fear has fallen below <see cref="CivilianParams.CalmBelow"/>.</summary>
        [SharedAiCondition]
        public static bool Calm(ref CivilianParams p, Entity self, EntityRepository world)
            => CivilianMind.FearOf(world, self) < p.CalmBelow;

        /// <summary>He is hurt: his injury is past <see cref="InjuryState.Ok"/> (wounded, incapacitated or dead — the type's thresholds).</summary>
        [SharedAiCondition]
        public static bool Wounded(ref CivilianParams p, Entity self, EntityRepository world)
            => world.HasComponent<Health>(self) && Injury.StateOf(world, self) != InjuryState.Ok;

        /// <summary>A spot within <paramref name="radius"/> of <paramref name="home"/>, uniform over the disc, the same for the same
        /// man, leg and time on every run.</summary>
        public static Vector3 WanderPoint(Vector3 home, float radius, int entityIndex, int leg, double now)
        {
            var rng = SimRng.FromSim(entityIndex, 0x22350 + leg, (float)now);
            float angle = rng.NextSingle() * MathF.PI * 2f;
            float r = radius * MathF.Sqrt(rng.NextSingle());
            return new Vector3(home.X + r * MathF.Cos(angle), home.Y + r * MathF.Sin(angle), home.Z);
        }
    }
}
