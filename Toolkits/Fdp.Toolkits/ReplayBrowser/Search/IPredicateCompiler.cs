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
        /// Walks the predicate tree and returns the set of component types that MUST be
        /// present on an entity for the predicate to possibly match.
        /// Only AND-compound roots contribute; OR-compounds are excluded because they
        /// require at least one of their children, not all.
        /// Used by the search service to build EntityQuery filters and short-circuit frames.
        /// </summary>
        IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto root);
    }
}
