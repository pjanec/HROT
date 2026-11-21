using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.ReplayBrowser.Search.Conditions
{
    /// <summary>
    /// ⭐ U-14 / C2 (docs/UX/DESIGN_Condition_Editor.md §2–§3) — the friendly view is a PROJECTION of the ONE condition language: rows are rebuilt from the DTO
    /// every time it opens (<see cref="FromDto"/>) and written back as a DTO (<see cref="ToDto"/>). ⭐ Anything the catalogue does not recognise — a bound field,
    /// a nested group, a debugging kind, a fact about the rule itself — becomes ONE Custom row holding the DTO untouched, so nothing is lost or rewritten.
    /// <para>Two shapes have two readings and come back in their canonical one (same meaning, same DTO): <i>none of S is not X</i> reads back as <i>all of S
    /// are X</i>; a one-row <i>None of these</i> group reads back as the row negated. <i>At least 1</i> reads back as <i>any</i>.</para>
    /// </summary>
    public static class RowProjection
    {
        /// <summary>The condition the rows say, joined by <paramref name="op"/>; one row under <c>And</c> is that row's DTO alone.</summary>
        public static SearchPredicateDto ToDto(IReadOnlyList<ConditionRow> rows, LogicalOperator op = LogicalOperator.And)
        {
            if (rows.Count == 1 && op == LogicalOperator.And) return ToDto(rows[0]);
            var c = new CompoundPredicateDto { Operator = op };
            for (int i = 0; i < rows.Count; i++) c.Conditions.Add(ToDto(rows[i]));
            return c;
        }

        /// <summary>One row's DTO: the fact's leaf (negated in a one-child <c>None</c>), wrapped by WHO.</summary>
        public static SearchPredicateDto ToDto(ConditionRow row)
        {
            if (row.IsCustom) return row.Custom ?? throw new ArgumentException("A Custom row carries its DTO.", nameof(row));
            var fact = FactCatalogue.Find(row.Fact) ?? throw new ArgumentException($"Unknown condition fact '{row.Fact}'.", nameof(row));
            SearchPredicateDto expr = fact.Build(row.Values);
            if (row.Negated) expr = Not(expr);
            var who = row.Who;
            switch (who.Kind)
            {
                case SubjectKind.World: return expr;
                case SubjectKind.One: return new OnEntityPredicateDto { Ref = who.One, Predicate = expr };
            }
            SearchPredicateDto set = who.Set == SetKind.Side
                ? FactCatalogue.Match(typeof(EntityInfo), nameof(EntityInfo.ForceId), (byte)who.Side, (byte)who.Side)
                : new InUnitPredicateDto { Unit = who.Unit, IncludeSelf = true };
            var body = who.Kind == SubjectKind.All ? Not(expr) : expr;   // all of S are X ⇔ none of S is not X
            var (min, max) = who.Kind switch
            {
                SubjectKind.Any => (1, int.MaxValue),
                SubjectKind.AtLeast => (Math.Max(1, who.N), int.MaxValue),
                _ => (0, 0),
            };
            return new CountMatchingPredicateDto
            {
                Min = min, Max = max,
                Predicate = new CompoundPredicateDto { Operator = LogicalOperator.And, Conditions = { set, body } },
            };
        }

        /// <summary>The rows <paramref name="dto"/> says and how they join; an unknown shape is one Custom row (the DTO untouched).</summary>
        public static (List<ConditionRow> Rows, LogicalOperator Op) FromDto(SearchPredicateDto? dto)
        {
            var rows = new List<ConditionRow>();
            if (dto is null) return (rows, LogicalOperator.And);
            if (dto is CompoundPredicateDto c && Plain(c))
            {
                if (c.Operator == LogicalOperator.None && c.Conditions.Count == 1 && RowOf(dto) is { IsCustom: false } single)
                { rows.Add(single); return (rows, LogicalOperator.And); }   // a one-row None group IS the row negated
                for (int i = 0; i < c.Conditions.Count; i++) rows.Add(RowOf(c.Conditions[i]));
                return (rows, c.Operator);
            }
            rows.Add(RowOf(dto));
            return (rows, LogicalOperator.And);
        }

        /// <summary>One DTO as one row, or a Custom row.</summary>
        public static ConditionRow RowOf(SearchPredicateDto d)
        {
            switch (d)
            {
                case OnEntityPredicateDto o when Unbound(o) && o.Predicate != null
                                                 && FactOf(o.Predicate) is { } f && !f.Entry.WorldOnly:
                    return new ConditionRow(Subject.Thing(o.Ref), f.Entry.Id, f.Negated, f.Values);
                case CountMatchingPredicateDto cm when Unbound(cm) && cm.WithComponent == null && cm.Predicate is CompoundPredicateDto and
                                                       && Plain(and) && and.Operator == LogicalOperator.And && and.Conditions.Count == 2
                                                       && SetOf(and.Conditions[0]) is { } set:
                {
                    var body = and.Conditions[1];
                    SubjectKind kind;
                    if (cm.Min == 0 && cm.Max == 0)
                    {
                        if (Negation(body) is { } inner) { kind = SubjectKind.All; body = inner; }
                        else kind = SubjectKind.None;
                    }
                    else if (cm.Max == int.MaxValue && cm.Min >= 1) kind = cm.Min == 1 ? SubjectKind.Any : SubjectKind.AtLeast;
                    else break;
                    if (FactOf(body) is not { } f || f.Entry.WorldOnly) break;
                    var who = set with { Kind = kind, N = kind == SubjectKind.AtLeast ? cm.Min : 1 };
                    return new ConditionRow(who, f.Entry.Id, f.Negated, f.Values);
                }
                default:
                    if (FactOf(d) is { } w && w.Entry.WorldOnly) return new ConditionRow(Subject.World, w.Entry.Id, w.Negated, w.Values);
                    break;
            }
            return ConditionRow.OfCustom(d);
        }

        private readonly record struct Fact(FactEntry Entry, bool Negated, FactValues Values);

        private static Fact? FactOf(SearchPredicateDto d)
        {
            bool negated = false;
            if (Negation(d) is { } inner) { negated = true; d = inner; }
            var entry = FactCatalogue.Recognise(d, out var values);
            if (entry == null || (negated && !entry.Negatable)) return null;
            return new Fact(entry, negated, values);
        }

        private static Subject? SetOf(SearchPredicateDto d)
        {
            if (FactCatalogue.IsMatch(d, typeof(EntityInfo), nameof(EntityInfo.ForceId), out double lo, out double hi) && lo == hi && Unbound(d))
                return Subject.OfSide(SubjectKind.Any, (ForceId)(byte)lo);
            if (d is InUnitPredicateDto u && u.IncludeSelf && Unbound(u)) return Subject.OfUnit(SubjectKind.Any, u.Unit);
            return null;
        }

        private static SearchPredicateDto Not(SearchPredicateDto d) => new CompoundPredicateDto { Operator = LogicalOperator.None, Conditions = { d } };

        /// <summary>The child of a plain one-child <c>None</c> (the negation), else null.</summary>
        private static SearchPredicateDto? Negation(SearchPredicateDto d)
            => d is CompoundPredicateDto { Operator: LogicalOperator.None, Conditions.Count: 1 } n && Plain(n) ? n.Conditions[0] : null;

        private static bool Plain(CompoundPredicateDto c) => c.ReadOnlyChildIndices.Count == 0 && Unbound(c);
        private static bool Unbound(SearchPredicateDto d) => d.Bind is not { Count: > 0 };
    }
}
