using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
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
    /// ⭐⭐ rules S4 ① (📄 docs/DESIGN_Scenario_Rules.md §00.11; SR-111) — <c>SelectionMean</c>: the weighted mean of a SELECTION of statuses, not of its parent-linked children.
    /// An objective has ONE <c>Parent</c>, and a man's <c>Member standards</c> root already belongs to his commander's evaluation — so "All riflemen" cannot adopt it (a second
    /// parent would be counted twice). It tallies through the SAME <see cref="ObjectiveChildren.Of"/> with a membership predicate and the SAME
    /// <see cref="WeightedMeanBehavior.Aggregate"/>; the selection is made live every tick:
    /// <list type="bullet">
    /// <item><c>by: role</c> — the member ROOTS directly under the scope (statuses whose Parent is the scope) whose subject's role key (<see cref="MemberEvaluationBehavior.RoleKey"/>) is
    /// the key. A root, once matched, is REMEMBERED (≤ 16, by network id) so a casualty (his subject gone) keeps his result in the mean.</item>
    /// <item><c>by: standard</c> — every standard (<see cref="IsStandard"/>) titled the key inside the members' subtrees.</item>
    /// </list>
    /// </summary>
    public static class SelectionMeanBehavior
    {
        public const string Kind = BehaviorNames.SelectionMean;
        public const byte ByRole = 0, ByStandard = 1;
        /// <summary>How far under the scope a standard may sit: root (1) → standard (2) → a sub-standard (3).</summary>
        public const int MaxDepth = 3;
        /// <summary>The cap of remembered roots (a roster holds 16).</summary>
        public const int MaxRoots = 16;

        private static readonly FixedString32 SelectionKind = new(Kind), EvaluationKind = new(BehaviorNames.MemberEvaluation);

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public byte By; public FixedString64 Key; public EntityRef Scope; public float PassScore;
            /// <summary>Working state (recorded with the blackboard): the member roots matched so far (network ids), and how many.</summary>
            public Ids16 Roots; public int RootCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SelectionMeanBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.SelectionMean)]
        public static void Resolve(in SelectionMeanParamsJsonDto authored, ref SelectionMeanBlackboard bb, EntityRepository world, Entity self)
        {
            var problems = ValidateParams(in authored);
            if (problems.Count > 0) throw new InvalidOperationException("SelectionMean: " + string.Join("; ", problems));
            var common = ObjectiveCommonBlock.From(in authored.Common);
            bb.Block = new Block
            {
                Common = common, By = ByOf(authored.By), Key = new FixedString64(authored.Key), Scope = authored.Scope.Ref,
                PassScore = ObjectiveRules.PassOf(Kind, ObjectiveRole.Objective, common.Pass),
            };
        }

        [BehaviorValidator(BehaviorNames.SelectionMean)]
        public static IEnumerable<string> Validate(string json)
        {
            SelectionMeanParamsJsonDto dto;
            try { dto = JsonSerializer.Deserialize<SelectionMeanParamsJsonDto>(json, BehaviorParams.JsonOptions); }
            catch (JsonException ex) { return new[] { $"params are not valid JSON — {ex.Message}" }; }
            return ValidateParams(in dto);
        }

        private static List<string> ValidateParams(in SelectionMeanParamsJsonDto a)
        {
            var problems = new List<string>();
            // a "^param" is a macro slot's binding, not yet a value (the sweep validates the macro files as written)
            if (a.By is not ("role" or "standard") && a.By?.StartsWith('^') != true)
                problems.Add("'by' must be \"role\" or \"standard\".");
            if (string.IsNullOrWhiteSpace(a.Key)) problems.Add("'key' is required (a role name, or a standard's title).");
            else if (a.Key.Length > 63) problems.Add($"'key' is {a.Key.Length} characters; at most 63.");
            return problems;
        }

        private static byte ByOf(string? by) => by == "standard" ? ByStandard : ByRole;

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run, ObjectiveHost.CloseMode.Composite);
            if (f.Reset) { b.Roots = default; b.RootCount = 0; }
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            var member = Selection(ref b, world);
            if (ObjectiveChildren.Evaluates(in s))
            {
                WeightedMeanBehavior.Aggregate(world, self, ref s, b.PassScore, f.Now, Kind, "", member);
                if (b.By == ByStandard) RawMeans(world, self, member, ref s);
            }
            ObjectiveHost.CloseComposite(world, self, ref s, f.Now, member);
            return NodeStatus.Running;
        }

        /// <summary>
        /// ⭐ S4 ⑤ — a STANDARD mean reads like the standard itself: its <c>Value</c> / <c>Target</c> are the (weighted) means of the selected standards' own Value and Target (the debrief's
        /// "squad's mean of the same measure" next to the man's Value / Target); its <c>Score</c> stays the mean of the normalised scores, GO = <c>Score ≥ Pass</c>. Over the scored, non-skipped ones only.
        /// </summary>
        private static void RawMeans(EntityRepository world, Entity self, Func<Entity, bool> member, ref ObjectiveStatus s)
        {
            double value = 0, target = 0, weight = 0;
            foreach (var e in world.Query().With<ObjectiveLinks>().With<ObjectiveStatus>().Build())
            {
                if (e == self || !member(e)) continue;
                ref readonly var c = ref world.GetComponentRO<ObjectiveStatus>(e);
                if (c.Scored == 0 || c.State == ObjectiveState.Skipped) continue;
                double w = c.Weight > 0f ? c.Weight : 1f;
                value += w * c.Value; target += w * c.Target; weight += w;
            }
            if (weight <= 0) return;
            s.Value = (float)(value / weight); s.Target = (float)(target / weight);
        }

        /// <summary>The membership predicate of this mean (the one <see cref="ObjectiveChildren.Of"/> takes).</summary>
        private static Func<Entity, bool> Selection(ref Block b, EntityRepository world)
        {
            var scope = b.Scope.Resolve(world);
            if (scope.IsNull || !world.IsAlive(scope)) return static _ => false;
            var key = b.Key;
            if (b.By == ByStandard)
                return e => world.GetComponentRO<ObjectiveStatus>(e).Title == key && IsStandard(world, e, scope);

            string role = key.ToString();
            foreach (var e in world.Query().With<ObjectiveLinks>().With<ObjectiveStatus>().Build())   // remember the roots of this role (a casualty's subject is gone later)
            {
                if (!IsRootOf(world, e, scope) || !RoleMatches(world, e, role)) continue;
                int id = EntityRef.Of(world, e).NetworkId;
                if (id != 0 && !Has(in b, id) && b.RootCount < MaxRoots) b.Roots[b.RootCount++] = id;
            }
            var roots = b.Roots; int count = b.RootCount;
            return e =>
            {
                if (!IsRootOf(world, e, scope)) return false;
                int id = EntityRef.Of(world, e).NetworkId;
                for (int i = 0; i < count; i++) if (roots[i] == id && id != 0) return true;
                return RoleMatches(world, e, role);
            };
        }

        private static bool Has(in Block b, int id)
        {
            for (int i = 0; i < b.RootCount; i++) if (b.Roots[i] == id) return true;
            return false;
        }

        /// <summary>A member's root: a status whose Parent is the scope (the evaluation), other than a mean the evaluation itself inserted.</summary>
        public static bool IsRootOf(EntityRepository world, Entity e, Entity scope)
            => world.GetComponentRO<ObjectiveLinks>(e).Parent.Resolve(world) == scope && world.GetComponentRO<ObjectiveStatus>(e).Kind != SelectionKind;

        private static bool RoleMatches(EntityRepository world, Entity root, string role)
        {
            var subject = world.GetComponentRO<ObjectiveLinks>(root).Subject.Resolve(world);
            return !subject.IsNull && world.IsAlive(subject) && MemberEvaluationBehavior.RoleKey(world, subject) == role;
        }

        /// <summary>
        /// A STANDARD of the members under <paramref name="scope"/>: a status 2–<see cref="MaxDepth"/> levels below it by the Parent chain (the member's root is level 1), that is not itself a
        /// <c>MemberEvaluation</c> or a mean, and whose chain does not pass through a NESTED <c>MemberEvaluation</c> (a squad's mean is its own men's).
        /// </summary>
        public static bool IsStandard(EntityRepository world, Entity e, Entity scope)
        {
            var kind = world.GetComponentRO<ObjectiveStatus>(e).Kind;
            if (kind == SelectionKind || kind == EvaluationKind) return false;
            var cur = e;
            for (int depth = 1; depth <= MaxDepth; depth++)
            {
                if (!world.HasComponent<ObjectiveLinks>(cur)) return false;
                var parent = world.GetComponentRO<ObjectiveLinks>(cur).Parent.Resolve(world);
                if (parent.IsNull || !world.IsAlive(parent)) return false;
                if (parent == scope) return depth >= 2;
                if (!world.HasComponent<ObjectiveStatus>(parent) || world.GetComponentRO<ObjectiveStatus>(parent).Kind == EvaluationKind) return false;
                cur = parent;
            }
            return false;
        }

        [Behavior(BehaviorNames.SelectionMean, Params = typeof(SelectionMeanParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.SelectionMean, Curated = true, Group = "Library/Objectives", ParamsType = typeof(SelectionMeanBlackboard))]
        public static BTreeBuilder<SelectionMeanBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SelectionMeanBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
