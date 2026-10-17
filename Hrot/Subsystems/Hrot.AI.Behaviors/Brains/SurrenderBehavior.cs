using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Attributes;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2241</c> ① (AW-5, AW-21; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §5h.5) — <c>Surrender</c>: he stops, lays every weapon
    /// down (<see cref="WeaponLaying.LayDownAll"/>, backend B-10) and raises his hands (<see cref="GestureKind.HandsUp"/>) — out of the fight
    /// (AW-21). After <see cref="SurrenderParams.Seconds"/> (0 = for good) the HSM reaches its Final state: the weapons come up, the hands
    /// down, and the behaviour finishes so the next mission task runs (the feint). Leaving early (a new order) undoes it the same way.
    /// </summary>
    public static class SurrenderBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SurrenderParams { public float Seconds; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SurrenderState { public double Since; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SurrenderBlackboard
        {
            public SurrenderParams Params;
            public SurrenderState State;
        }

        [BehaviorResolver(BehaviorNames.Surrender)]
        public static void Resolve(in SurrenderParamsJsonDto authored, ref SurrenderBlackboard block, EntityRepository world, Entity self)
            => block.Params = new SurrenderParams { Seconds = authored.Seconds > 0f ? authored.Seconds : 0f };

        [Behavior(BehaviorNames.Surrender, Params = typeof(SurrenderParamsJsonDto), Categories = BehaviorCategory.AllMilitary)]
        [HsmDefinition(BehaviorNames.Surrender, Curated = true, Group = "Library/Orders", ParamsType = typeof(SurrenderBlackboard))]
        public static StateMachineGraph BuildSurrender()
        {
            var builder = new HsmBuilder("Surrender_HSM");
            var surrendered = builder.State("Surrendered").Initial()
                .Activity<SurrenderBlackboard, SurrenderParams, SurrenderState>(bb => bb.Params, bb => bb.State, Surrendered);
            builder.State("Done").Final();
            surrendered.On(0).GoTo("Done").Guard<SurrenderBlackboard, SurrenderParams, SurrenderState>(bb => bb.Params, bb => bb.State, HeldLongEnough).Polled();
            return builder.Build();
        }

        /// <summary>Surrendered: he stands still, his weapons laid down, his hands up — every tick, so a weapon taken up by anything else
        /// goes down again while he surrenders.</summary>
        [SharedAiAction]
        public static NodeStatus Surrendered(ref SurrenderParams p, ref SurrenderState ws, Entity self, EntityRepository world)
        {
            if (ws.Since <= 0) ws.Since = world.SimulationTime > 0 ? world.SimulationTime : 1e-6;
            LocomotionMoveTo.Stop(world, self);
            if (!WeaponLaying.AllLaidDown(world, self)) WeaponLaying.LayDownAll(world, self);
            if (Gestures.Of(world, self) != GestureKind.HandsUp) Gestures.Set(world, self, GestureKind.HandsUp);
            return NodeStatus.Running;
        }

        /// <summary>Leaving the surrender (its time is up, or an order replaced it): he takes his weapons up and lowers his hands.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.SurrenderBehavior.Surrendered")]
        public static void Deactivate_Surrendered(ref SurrenderParams p, ref SurrenderState ws, Entity self, EntityRepository world)
        {
            WeaponLaying.LayDownAll(world, self, down: false);
            Gestures.Set(world, self, GestureKind.None);
            ws = default;
        }

        /// <summary>The surrender has run its <see cref="SurrenderParams.Seconds"/> (never, when 0).</summary>
        [SharedAiCondition]
        public static bool HeldLongEnough(ref SurrenderParams p, ref SurrenderState ws, Entity self, EntityRepository world)
            => p.Seconds > 0f && ws.Since > 0 && world.SimulationTime - ws.Since >= p.Seconds;
    }
}
