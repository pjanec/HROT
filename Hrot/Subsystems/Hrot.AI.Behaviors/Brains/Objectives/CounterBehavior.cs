using System;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules S6 G-2 (📄 docs/DESIGN_Scenario_Rules.md §00.12) — <c>Counter</c>, a MEASURE (<see cref="ObjectiveRole.Measure"/>): <c>Value</c> starts at <c>start</c>
    /// (= its <c>Target</c>, so the score is value / start); the <c>AddToCounter</c> verb adds to it; each <c>deaths</c> rule adds <c>per</c> for every man of its side newly
    /// OUT of the fight (<see cref="ObjectiveCensus.DownOf"/>, counted once — a revived man refunds nothing). The value is kept within [<c>floor</c> (default 0), <c>ceiling</c>].
    /// A thin behaviour like every kind: it reads the census and writes <see cref="ObjectiveStatus"/> through <see cref="ObjectiveHost"/>.
    /// </summary>
    public static class CounterBehavior
    {
        public const string Kind = BehaviorNames.Counter;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public float Start, Floor, Ceiling; public byte HasCeiling;
            /// <summary>The death rules (at most two; a Neutral side is unused): who, and what each man down adds.</summary>
            public ForceId Side0, Side1; public float Per0, Per1;
            /// <summary>Working state (recorded with the blackboard): the downs already counted per rule; whether the start value was written; the value last shown in the reason.</summary>
            public int LastDowns0, LastDowns1; public byte Started; public float Shown;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CounterBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.Counter)]
        public static void Resolve(in CounterParamsJsonDto authored, ref CounterBlackboard bb, EntityRepository world, Entity self)
        {
            var b = new Block
            {
                Common = ObjectiveCommonBlock.From(in authored.Common), Start = authored.Start,
                Floor = authored.Floor ?? 0f, Ceiling = authored.Ceiling ?? 0f, HasCeiling = (byte)(authored.Ceiling.HasValue ? 1 : 0),
            };
            var d = authored.Deaths;
            if (d is { Length: > 0 }) { b.Side0 = d[0].Side; b.Per0 = d[0].Per; }
            if (d is { Length: > 1 }) { b.Side1 = d[1].Side; b.Per1 = d[1].Per; }
            bb.Block = b;
        }

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Measure, in b.Common, ref b.Run);
            if (!f.Ready) return NodeStatus.Running;   // ⭐ CE-4568 — its components land at the merge; it begins next tick
            if (f.Reset) b.Started = 0;   // SR-114: a reset starts the count again (the value is written anew below)
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                if (b.Started == 0)
                {
                    // the start value; the men already down NOW are the baseline (only men who go down from here are "newly" out of the fight)
                    s.Value = b.Start; s.Target = b.Start; b.Started = 1;
                    b.LastDowns0 = ObjectiveCensus.DownOf(world, b.Side0); b.LastDowns1 = ObjectiveCensus.DownOf(world, b.Side1);
                    b.Shown = float.NaN;
                }
                else
                {
                    s.Value += NewDowns(b.Side0, b.Per0, ref b.LastDowns0, world) + NewDowns(b.Side1, b.Per1, ref b.LastDowns1, world);
                }
                s.Value = Math.Max(s.Value, b.Floor);
                if (b.HasCeiling != 0) s.Value = Math.Min(s.Value, b.Ceiling);
                if (s.Value != b.Shown || s.Reason.Length == 0)
                {
                    b.Shown = s.Value;
                    ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active, $"{s.Value:0} of {b.Start:0}", f.Now);
                }
            }
            ObjectiveRules.WriteScore(ref s);
            return NodeStatus.Running;
        }

        /// <summary>The change a rule makes this tick: <c>per</c> × the men newly down (a revive or a removed corpse lowers the census; nothing is refunded).</summary>
        private static float NewDowns(ForceId side, float per, ref int last, EntityRepository world)
        {
            if (side == ForceId.Neutral) return 0f;
            int downs = ObjectiveCensus.DownOf(world, side);
            // ⚠ the census FOLLOWS the downs (not a high-water mark): a corpse that is cleaned up, or a man revived, lowers it — and the NEXT man to go down must count again.
            // A high-water mark would swallow every death until the census climbed back above the old peak (respawn waves + corpse cleanup ⇒ the drain goes silent).
            float delta = downs > last ? per * (downs - last) : 0f;
            last = downs;
            return delta;
        }

        [Behavior(BehaviorNames.Counter, Params = typeof(CounterParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.Counter, Curated = true, Group = "Library/Objectives", ParamsType = typeof(CounterBlackboard))]
        public static BTreeBuilder<CounterBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<CounterBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
