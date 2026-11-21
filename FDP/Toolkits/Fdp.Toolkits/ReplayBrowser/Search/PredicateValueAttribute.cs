using System;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐ CE-4218 (rules view R5a, RE-3 / A4) — marks a <see cref="SearchPredicateDto"/> member as the VALUE leaf of a property match: a Numeric / String predicate whose kind and range depend on the SIBLING component / property the match names
    /// (<c>PropertyMatchDto.Predicate</c>, <c>BehaviorParamPredicateDto.Predicate</c>, <c>BlueprintVariablePredicateDto.Predicate</c>). The value-leaf field editor draws only members that carry it.
    /// ⛔ It replaces the old gate by the member NAME <c>"Predicate"</c>, which also caught <c>OnEntityPredicateDto.Predicate</c> and <c>CountMatchingPredicateDto.Predicate</c> — sub-trees, not values — and drew "(Select PropertyPath first)" over them.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class PredicateValueAttribute : Attribute { }
}
