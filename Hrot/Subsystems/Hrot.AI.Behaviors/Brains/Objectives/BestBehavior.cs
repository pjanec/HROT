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
    /// ⭐ rules S7 P-7 (📄 docs/DESIGN_Scenario_Rules.md §00.13) — <c>Best</c>, an award. Among the DESCENDANTS of <c>among</c> (the <c>ObjectiveLinks.Parent</c> chain, ≤ <see cref="MaxDepth"/> levels) that are
    /// measures titled <c>measure</c>, the best <c>Value</c> that passes <c>min</c> (≥ min, or ≤ min when <c>lowerIsBetter</c>; no <c>min</c> = no floor). While the exercise runs it is Active and shows the
    /// leader; once it is closing and every candidate has closed (their values are final) it settles: <b>Achieved</b> with <c>ObjectiveLinks.Subject</c> = the winner's own subject (his man) when one qualifies,
    /// else <b>Failed</b> "nobody qualified". The winner is a READ of that link — no second store.
    /// </summary>
    public static class BestBehavior
    {
        public const string Kind = BehaviorNames.Best;
        /// <summary>How many <c>Parent</c> hops below <c>among</c> a candidate may be (Evaluation → Score → Count is 2 hops).</summary>
        public const int MaxDepth = 4;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public EntityRef Among; public FixedString64 Measure; public float Min; public byte HasMin, LowerIsBetter;
            /// <summary>⭐ S7 ⑤ — <c>also</c>: the sibling measure's title and its floor (<c>HasAlso</c> 0 = none).</summary>
            public FixedString64 AlsoMeasure; public float AlsoMin; public byte HasAlso;
            /// <summary>Working state (recorded with the blackboard): the winner once settled (the links are re-copied every tick, so it is re-written after).</summary>
            public byte Settled; public EntityRef Winner; public float ShownValue; public int ShownN;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BestBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.Best)]
        public static void Resolve(in BestParamsJsonDto authored, ref BestBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block
            {
                Common = ObjectiveCommonBlock.From(in authored.Common), Among = authored.Among.Ref, Measure = new FixedString64(authored.Measure ?? string.Empty),
                Min = authored.Min ?? 0f, HasMin = (byte)(authored.Min.HasValue ? 1 : 0), LowerIsBetter = (byte)(authored.LowerIsBetter ? 1 : 0), ShownValue = float.NaN,
                HasAlso = (byte)(authored.Also is { Measure: { Length: > 0 } } ? 1 : 0), AlsoMeasure = new FixedString64(authored.Also?.Measure ?? string.Empty), AlsoMin = authored.Also?.Min ?? 0f,
            };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run, ObjectiveHost.CloseMode.Hold);   // Hold: it settles itself, after the measures it reads have closed
            if (f.Reset) { b.Settled = 0; b.Winner = default; b.ShownValue = float.NaN; }
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                var among = b.Among.Resolve(world);
                var best = Find(world, self, among, in b, out int candidates, out int closed, out bool found, out float value);
                bool closing = ExerciseResult.PhaseOf(world) != ExercisePhase.Running;
                if (closing && closed == candidates)
                {
                    // every measure it compares is final: settle once
                    b.Settled = 1;
                    if (found) { b.Winner = world.GetComponentRO<ObjectiveLinks>(best).Subject; s.Value = value; ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Achieved, Reason(world, in b, value), f.Now); }
                    else ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Failed, "nobody qualified", f.Now);
                    ObjectiveRules.OnClose(ref s, CloseRule.Ordinary, f.Now);
                }
                else
                {
                    if (found) { s.Value = value; b.Winner = world.GetComponentRO<ObjectiveLinks>(best).Subject; }
                    else { s.Value = 0f; b.Winner = default; }
                    if (MeasureHost.Changed(ref b.ShownValue, ref b.ShownN, found ? value : float.NaN, found ? 1 : 0, in s))
                        ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active, found ? Reason(world, in b, value) : "nobody qualifies yet", f.Now);
                }
            }
            else if (s.Closed == 0 && ExerciseResult.PhaseOf(world) != ExercisePhase.Running)
                ObjectiveRules.OnClose(ref s, CloseRule.Ordinary, f.Now);   // never active (Pending / Inactive): Skipped, so a composite above can close
            // the winner is the status's Subject once settled; Enter re-copies the authored link every tick, so write it after
            if (b.Settled != 0) world.GetComponentRW<ObjectiveLinks>(self).Subject = b.Winner;
            ObjectiveRules.WriteScore(ref s);
            return NodeStatus.Running;
        }

        private static string Reason(EntityRepository world, in Block b, float value)
        {
            var who = b.Winner.Resolve(world);
            string name = !who.IsNull && world.IsAlive(who) && world.HasComponent<EntityInfo>(who) ? world.GetComponentRO<EntityInfo>(who).Name.ToString() : "";
            if (name.Length == 0) name = $"#{b.Winner.NetworkId}";
            return $"{name} — {value:0.##}";
        }

        /// <summary>The best qualifying measure under <paramref name="among"/> (Entity.Null when none), and how many candidates exist / are closed.</summary>
        private static Entity Find(EntityRepository world, Entity self, Entity among, in Block b, out int candidates, out int closed, out bool found, out float value)
        {
            candidates = 0; closed = 0; found = false; value = 0f;
            var best = Entity.Null;
            if (among.IsNull || !world.IsAlive(among)) return best;
            bool lower = b.LowerIsBetter != 0;
            foreach (var e in world.Query().With<ObjectiveLinks>().With<ObjectiveStatus>().Build())
            {
                if (e == self) continue;
                ref readonly var c = ref world.GetComponentRO<ObjectiveStatus>(e);
                if (c.Role != ObjectiveRole.Measure || !c.Title.Equals(b.Measure) || !Under(world, e, among)) continue;
                candidates++;
                bool alsoOk = true, alsoClosed = true;
                if (b.HasAlso != 0) alsoOk = Sibling(world, e, in b, out alsoClosed);   // ⭐ S7 ⑤ — his sibling measure's floor
                if (c.Closed != 0 && alsoClosed) closed++;   // the sibling's value must be final too
                if (!alsoOk) continue;
                if (b.HasMin != 0 && (lower ? c.Value > b.Min : c.Value < b.Min)) continue;
                if (!found || (lower ? c.Value < value : c.Value > value)) { found = true; value = c.Value; best = e; }
            }
            return best;
        }

        /// <summary>⭐ S7 ⑤ — whether <paramref name="candidate"/>'s SIBLING (a measure titled <c>also.measure</c> with the same <c>Parent</c>) has Value ≥ <c>also.min</c>; <paramref name="closed"/> says the sibling is closed (no sibling ⇒ false and closed).</summary>
        private static bool Sibling(EntityRepository world, Entity candidate, in Block b, out bool closed)
        {
            closed = true;
            var parent = world.GetComponentRO<ObjectiveLinks>(candidate).Parent;
            if (parent.IsNone) return false;
            foreach (var e in world.Query().With<ObjectiveLinks>().With<ObjectiveStatus>().Build())
            {
                if (e == candidate) continue;
                ref readonly var c = ref world.GetComponentRO<ObjectiveStatus>(e);
                if (c.Role != ObjectiveRole.Measure || !c.Title.Equals(b.AlsoMeasure) || world.GetComponentRO<ObjectiveLinks>(e).Parent != parent) continue;
                closed = c.Closed != 0;
                return c.Value >= b.AlsoMin;
            }
            return false;
        }

        /// <summary>Whether <paramref name="e"/> is a descendant of <paramref name="among"/> within <see cref="MaxDepth"/> parent hops.</summary>
        private static bool Under(EntityRepository world, Entity e, Entity among)
        {
            for (int d = 0; d < MaxDepth; d++)
            {
                if (!world.HasComponent<ObjectiveLinks>(e)) return false;
                var p = world.GetComponentRO<ObjectiveLinks>(e).Parent.Resolve(world);
                if (p.IsNull) return false;
                if (p == among) return true;
                e = p;
            }
            return false;
        }

        [Behavior(BehaviorNames.Best, Params = typeof(BestParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.Best, Curated = true, Group = "Library/Objectives", ParamsType = typeof(BestBlackboard))]
        public static BTreeBuilder<BestBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<BestBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
