using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Fdp.Toolkit.ScenarioRules
{
    /// <summary>
    /// ⭐ rules V-3 (📄 docs/DESIGN_Scenario_Rules.md §00.10) — a trigger's <c>when</c> FLATTENED into at most eight leaves, each compiled ONCE: its LEVEL predicate through
    /// <see cref="IPredicateCompiler.CompileLevelPredicate"/> (the very arm the trigger's own condition uses) and its number through <see cref="IPredicateCompiler.CompileLeafMeasure"/>
    /// (the other half of that arm). The tree is descended through <c>Compound</c> only; every other predicate is ONE leaf — an <c>OnEntity</c> with its inner predicate included
    /// (labelled with its target's authored ref text and the inner leaf). A plan holds no world: it takes the repository and the entity as arguments, so it is cached by the
    /// <c>when</c> JSON like the trigger's compiled condition (<c>TriggerLeaves</c>).
    /// </summary>
    public sealed class RuleLeafPlan
    {
        /// <summary>The most leaves a plan (and <see cref="RuleLeaves"/>) holds.</summary>
        public const int MaxLeaves = 8;

        private sealed record Leaf(string Label, Func<EntityRepository, Entity, bool> Test, Func<EntityRepository, Entity, double>? Measure);

        private readonly Leaf[] _leaves;

        private RuleLeafPlan(Leaf[] leaves, bool truncated) { _leaves = leaves; Truncated = truncated; }

        /// <summary>The leaves in the plan.</summary>
        public int Count => _leaves.Length;

        /// <summary>The <c>when</c> had more than <see cref="MaxLeaves"/> leaves: the first eight are here.</summary>
        public bool Truncated { get; }

        /// <summary>The label of leaf <paramref name="i"/>.</summary>
        public string LabelOf(int i) => _leaves[i].Label;

        /// <summary>Flattens <paramref name="when"/> (≤ 8 leaves) and compiles each leaf once. ⛔ Throws when a leaf does not compile (as the trigger's own condition does).</summary>
        public static RuleLeafPlan Of(SearchPredicateDto when, IPredicateCompiler compiler)
        {
            var nodes = LeavesOf(when, out bool truncated);
            var leaves = new Leaf[nodes.Count];
            for (int i = 0; i < leaves.Length; i++)
                leaves[i] = new Leaf(LabelOf(nodes[i]), compiler.CompileLevelPredicate(nodes[i]), compiler.CompileLeafMeasure(nodes[i]));
            return new RuleLeafPlan(leaves, truncated);
        }

        /// <summary>
        /// ⭐ CE-4220 (R5b) — the leaves <paramref name="when"/> flattens into: descended through <see cref="CompoundPredicateDto"/> only, every other predicate ONE leaf, at most <see cref="MaxLeaves"/>.
        /// The ONE flattening — the plan below and the rule editor's live strip (which matches the form's leaves to the owner's <see cref="RuleLeaves"/> by <see cref="LabelOf(SearchPredicateDto)"/>) both use it.
        /// </summary>
        public static IReadOnlyList<SearchPredicateDto> LeavesOf(SearchPredicateDto? when, out bool truncated)
        {
            var leaves = new List<SearchPredicateDto>(MaxLeaves);
            truncated = false;
            if (when != null) Flatten(when, leaves, ref truncated);
            return leaves;
        }

        private static void Flatten(SearchPredicateDto node, List<SearchPredicateDto> into, ref bool truncated)
        {
            if (node is CompoundPredicateDto compound)
            {
                foreach (var child in compound.Conditions) Flatten(child, into, ref truncated);
                return;
            }
            if (into.Count >= MaxLeaves) { truncated = true; return; }
            into.Add(node);
        }

        /// <summary>The short label of one leaf: <c>Health.Current</c> · <c>@Bridge Health.Current</c> (an OnEntity: its target's authored ref + the inner leaf) · <c>ScenarioTime</c> · <c>Count Health</c> · …</summary>
        public static string LabelOf(SearchPredicateDto leaf) => leaf switch
        {
            PropertyMatchDto p => string.IsNullOrEmpty(p.PropertyPath) ? p.ComponentType?.Name ?? "Property" : $"{p.ComponentType?.Name}.{p.PropertyPath}",
            OnEntityPredicateDto o => $"{RefText(o.Ref)} {(o.Predicate is null ? "?" : LabelOf(o.Predicate))}",
            ScenarioTimePredicateDto => "ScenarioTime",
            InputAbovePredicateDto i => $"Input {i.Input}",
            CountMatchingPredicateDto c => $"Count {(c.Predicate is null ? "?" : LabelOf(c.Predicate))}",
            SpatialBoundingPredicateDto => "Spatial",
            LifecyclePredicateDto => "Lifecycle",
            StructuralPredicateDto s => s.ComponentType is null ? "Structural" : $"Has {s.ComponentType.Name}",
            TransientEventPredicateDto e => e.EventType is null ? "Event" : $"Event {e.EventType.Name}",
            _ => leaf.GetType().Name.Replace("PredicateDto", string.Empty),
        };

        private static string RefText(Fdp.Toolkit.Replication.EntityLink link)
            => link.Spec.IsNone ? link.Ref.ToString() : link.Spec.ToString();

        /// <summary>Evaluates every leaf on <paramref name="self"/> and writes the result into <paramref name="into"/> (Count, then each label / 0-1 / measured number).</summary>
        public void Sample(EntityRepository repo, Entity self, ref RuleLeaves into)
        {
            into.Count = (byte)_leaves.Length;
            for (int i = 0; i < _leaves.Length; i++)
            {
                var leaf = _leaves[i];
                bool holds = leaf.Test(repo, self);
                ref var slot = ref into.Leaves[i];
                slot.Label = new FixedString32(leaf.Label);
                slot.Value = (byte)(holds ? 1 : 0);
                slot.Measured = leaf.Measure is { } m ? (float)m(repo, self) : holds ? 1f : 0f;
            }
        }
    }
}
