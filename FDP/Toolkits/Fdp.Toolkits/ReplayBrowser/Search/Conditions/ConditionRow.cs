using System;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.ReplayBrowser.Search.Conditions
{
    /// <summary>⭐ U-14 / C2 (docs/UX/DESIGN_Condition_Editor.md §2) — WHO a row is about. A trigger evaluates its condition on ITSELF (a positionless rule
    /// entity), so a fact about a unit must be wrapped: one named thing ⇒ <c>OnEntity</c>; a set ⇒ <c>CountMatching</c>; the world (time) ⇒ no wrapper.</summary>
    public enum SubjectKind : byte { World, One, Any, AtLeast, All, None }

    /// <summary>The set a counted subject ranges over: a side, or a unit and everyone under it (<c>InUnit</c>).</summary>
    public enum SetKind : byte { Side, UnitsMen }

    /// <summary>WHO: the world, one thing (<see cref="One"/>), or any / at least <see cref="N"/> / all / none of a set.</summary>
    public sealed record Subject(SubjectKind Kind, EntityLink One = default, SetKind Set = SetKind.Side, ForceId Side = ForceId.Neutral, EntityLink Unit = default, int N = 1)
    {
        public static readonly Subject World = new(SubjectKind.World);
        public static Subject Thing(EntityLink thing) => new(SubjectKind.One, One: thing);
        public static Subject OfSide(SubjectKind kind, ForceId side, int n = 1) => new(kind, Set: SetKind.Side, Side: side, N: n);
        public static Subject OfUnit(SubjectKind kind, EntityLink unit, int n = 1) => new(kind, Set: SetKind.UnitsMen, Unit: unit, N: n);
        public bool IsCounted => Kind is SubjectKind.Any or SubjectKind.AtLeast or SubjectKind.All or SubjectKind.None;
    }

    /// <summary>The blanks a fact fills: a number (a threshold, a count, a time), a second number (an upper bound), an area, a side, an enum value.</summary>
    public readonly record struct FactValues(double Number = 0, double Number2 = double.MaxValue, AreaLink Area = default, ForceId Side = ForceId.Neutral, int State = 0);

    /// <summary>
    /// ⭐ U-14 / C2 — one friendly row: WHO · IS/HAS · VALUE. A PROJECTION of the DTO (<see cref="RowProjection"/>), never stored. <see cref="Fact"/> is a
    /// <see cref="FactCatalogue"/> id; <see cref="Negated"/> is the negated verb (<i>is not</i>, <i>below</i>, <i>destroyed</i>). A shape the catalogue does not
    /// know is ONE <see cref="FactCatalogue.Custom"/> row carrying the DTO untouched in <see cref="Custom"/>.
    /// </summary>
    public sealed record ConditionRow(Subject Who, string Fact, bool Negated = false, FactValues Values = default, SearchPredicateDto? Custom = null)
    {
        public bool IsCustom => Fact == FactCatalogue.Custom;
        public static ConditionRow OfCustom(SearchPredicateDto dto) => new(Subject.World, FactCatalogue.Custom, Custom: dto);
    }

    /// <summary>Where a fact is offered (R-4130): a rule and a mission task see the friendly facts; the replay search and breakpoints also the debugging kinds.</summary>
    [Flags]
    public enum ConditionContexts : byte
    {
        Rule = 1, Mission = 2, Search = 4, Breakpoint = 8,
        Authoring = Rule | Mission,
        Everywhere = Rule | Mission | Search | Breakpoint,
    }
}
