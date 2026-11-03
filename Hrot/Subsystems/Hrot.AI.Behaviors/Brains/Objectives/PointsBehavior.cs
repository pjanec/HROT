using System;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules S7 P-2 (📄 docs/DESIGN_Scenario_Rules.md §00.13) — <c>Points</c>: a MEASURE composite (a player's game score). Always Active, never Failed; every evaluation
    /// <c>Value</c> = Σ over its scored children (<c>ObjectiveLinks.Parent</c> = it) of <c>child.Value × child.Points</c> (<c>points</c> 0 = 1) — the ONE sum is
    /// <see cref="ObjectiveChildren.PointsOf"/>. <c>Score</c> = <c>Value / target</c> clamped to 0..1 (no <c>target</c> ⇒ 0). It closes once every child has (the
    /// composite close), so its final value is the sum of the children's FINAL values.
    /// </summary>
    public static class PointsBehavior
    {
        public const string Kind = BehaviorNames.Points;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public float Target; public float ShownValue; public int ShownN;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PointsBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.Points)]
        public static void Resolve(in PointsParamsJsonDto authored, ref PointsBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block { Common = ObjectiveCommonBlock.From(in authored.Common), Target = authored.Target ?? 0f, ShownValue = float.NaN };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Measure, in b.Common, ref b.Run, ObjectiveHost.CloseMode.Composite);
            if (f.Reset) b.ShownValue = float.NaN;
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                s.Value = ObjectiveChildren.PointsOf(world, self, out int n);
                s.Target = b.Target;
                if (MeasureHost.Changed(ref b.ShownValue, ref b.ShownN, s.Value, n, in s))
                    ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active, $"{s.Value:0.##} points", f.Now);
            }
            s.Score = b.Target > 0f ? Math.Clamp(s.Value / b.Target, 0f, 1f) : 0f;
            ObjectiveHost.CloseComposite(world, self, ref s, f.Now);
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.Points, Params = typeof(PointsParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.Points, Curated = true, Group = "Library/Objectives", ParamsType = typeof(PointsBlackboard))]
        public static BTreeBuilder<PointsBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<PointsBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
