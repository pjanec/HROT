using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// Compiles a <see cref="SearchPredicateDto"/> tree into a delegate that evaluates
    /// whether a given entity in a given repository satisfies the predicate.
    /// </summary>
    public interface IPredicateCompiler
    {
        /// <summary>
        /// Recursively compiles <paramref name="root"/> into a predicate function.
        /// The returned delegate is thread-safe and allocation-free on the hot path
        /// (assuming no matches on stationary frames).
        /// </summary>
        Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto root);

        /// <summary>
        /// ⭐ <c>CE-4164</c> (DESIGN_Scenario_Rules §00.9 S2-b) — the LEVEL entry point: the same compiler, the same arms, but the four
        /// specialised loop predicates answer a STATE instead of passing through (SR-158) — spatial: the entity's position is inside
        /// <c>Bounds</c> (<c>TriggerEvent</c> is ignored: an edge is the caller's job); lifecycle: the entity it identifies is alive; structural:
        /// the entity has the component (Removed: has not); transient event: such an event occurred THIS frame (naming this entity when the
        /// predicate points at an entity field). ONE implementation — <see cref="CompileComponentPredicate"/> is the Search mode of it.
        /// </summary>
        /// <remarks>A default that THROWS: <see cref="PredicateCompiler"/> is the one implementation; a test double that never asks for a level
        /// predicate need not implement it, and one that does ask fails loudly instead of getting a Search-mode answer.</remarks>
        Func<EntityRepository, Entity, bool> CompileLevelPredicate(SearchPredicateDto root)
            => throw new NotSupportedException($"{GetType().Name} has no level mode — only PredicateCompiler implements CompileLevelPredicate.");

        /// <summary>
        /// ⭐ rules V-3 (§00.10) — the NUMBER behind one leaf, for the rules view: a <c>PropertyMatch</c>'s property value (numeric / enum / bool), the scenario time of a
        /// <c>ScenarioTime</c>, the count of a <c>CountMatching</c>; an <c>OnEntity</c> reads its inner leaf on the entity it resolves to. <c>null</c> = the leaf has no number
        /// (spatial, lifecycle, structural, event, compound, a non-numeric property): the caller shows its 0/1. Same default-throws shape as <see cref="CompileLevelPredicate"/>.
        /// </summary>
        Func<EntityRepository, Entity, double>? CompileLeafMeasure(SearchPredicateDto leaf)
            => throw new NotSupportedException($"{GetType().Name} has no leaf measure — only PredicateCompiler implements CompileLeafMeasure.");

        /// <summary>
        /// Walks the predicate tree and returns the set of component types that MUST be
        /// present on an entity for the predicate to possibly match.
        /// Only AND-compound roots contribute; OR-compounds are excluded because they
        /// require at least one of their children, not all.
        /// Used by the search service to build EntityQuery filters and short-circuit frames.
        /// </summary>
        IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto root);
    }
}
