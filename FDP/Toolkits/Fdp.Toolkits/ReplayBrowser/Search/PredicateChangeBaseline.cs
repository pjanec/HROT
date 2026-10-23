using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐ <c>CE-1258</c> (BV-28) — the world a <see cref="SearchOperator.Changed"/> condition compares against, while one caller evaluates
    /// (the breakpoint system sets its pre-tick copy around the component path). Per thread; null = no baseline (chunk-granular).
    /// </summary>
    public static class PredicateChangeBaseline
    {
        [ThreadStatic] private static EntityRepository? _current;

        public static EntityRepository? Current => _current;

        /// <summary>Sets the baseline until the returned scope is disposed.</summary>
        public static Scope Use(EntityRepository? baseline)
        {
            var previous = _current;
            _current = baseline;
            return new Scope(previous);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly EntityRepository? _previous;
            internal Scope(EntityRepository? previous) => _previous = previous;
            public void Dispose() => _current = _previous;
        }
    }

    /// <summary>⭐ <c>CE-1258</c> (BV-28) — "stop when this unit changes its mind": the condition, for a breakpoint filtered to the unit.</summary>
    public static class BrainJournalBreak
    {
        /// <summary>The journal's <see cref="BrainJournal.Seq"/> changed — and, when <paramref name="kinds"/> is given, the newest entry is one of them.</summary>
        public static SearchPredicateDto Condition(params BrainJournalKind[] kinds)
        {
            var changed = new PropertyMatchDto
            {
                ComponentType = typeof(BrainJournal), PropertyPath = nameof(BrainJournal.Seq), Operator = SearchOperator.Changed,
                Predicate = new NumericPredicateDto(),
            };
            if (kinds is not { Length: > 0 }) return changed;
            var any = new CompoundPredicateDto { Operator = LogicalOperator.Or };
            foreach (var k in kinds)
                any.Conditions.Add(new PropertyMatchDto
                {
                    ComponentType = typeof(BrainJournal), PropertyPath = nameof(BrainJournal.LastKind), Operator = SearchOperator.Equals,
                    Predicate = new NumericPredicateDto { MinValue = (byte)k, MaxValue = (byte)k },
                });
            var all = new CompoundPredicateDto { Operator = LogicalOperator.And };
            all.Conditions.Add(changed);
            all.Conditions.Add(any);
            return all;
        }
    }
}
