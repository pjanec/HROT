using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.ReplayBrowser.Search.Conditions
{
    /// <summary>
    /// ⭐ U-14 / C2 (docs/UX/DESIGN_Condition_Editor.md §2) — THE ONE sentence renderer of a condition: the friendly editor's rows, the rule list, the Live
    /// strip (which matches leaves by this text), a collapsed node in the struct editor (<c>EditSummaries</c>) and breakpoints. A condition made only of
    /// debugging kinds keeps its technical summary (<see cref="Technical"/> — <c>BreakpointConditionSummarizer</c>, folded in here: one implementation).
    /// </summary>
    public static class ConditionSentence
    {
        /// <summary>The whole condition as a sentence: <i>Alpha 1-1 is inside the area and it is after 1:00</i>.</summary>
        public static string Of(SearchPredicateDto? dto, LiveRefScope? refs = null)
        {
            if (dto is null) return "(none)";
            var (rows, op) = RowProjection.FromDto(dto);
            bool anyFriendly = false;
            for (int i = 0; i < rows.Count; i++) anyFriendly |= !rows[i].IsCustom;
            if (!anyFriendly) return Technical(dto);
            var sb = new StringBuilder();
            if (op == LogicalOperator.None) sb.Append("none of: ");
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) sb.Append(op == LogicalOperator.And ? " and " : op == LogicalOperator.Or ? " or " : "; ");
                sb.Append(Of(rows[i], refs));
            }
            return sb.ToString();
        }

        /// <summary>One row: WHO + the fact's words (<i>at least 3 of Red side are destroyed</i>).</summary>
        public static string Of(ConditionRow row, LiveRefScope? refs = null)
        {
            if (row.IsCustom) return Technical(row.Custom);
            var fact = FactCatalogue.Find(row.Fact);
            if (fact == null) return row.Fact;
            string words = fact.Words(row.Values, row.Negated);
            var who = row.Who;
            if (who.Kind == SubjectKind.World) return Capital(words);
            if (who.Kind == SubjectKind.One) return $"{NameOf(who.One, refs)} {words}";
            string set = who.Set == SetKind.Side ? FactCatalogue.SideName(who.Side) + " side" : NameOf(who.Unit, refs) + "'s men";
            string quantity = who.Kind switch
            {
                SubjectKind.Any => "any of ",
                SubjectKind.AtLeast => $"at least {who.N} of ",
                SubjectKind.All => "all of ",
                _ => "none of ",
            };
            return Capital(quantity) + set + " " + (who.Kind is SubjectKind.All or SubjectKind.AtLeast ? Plural(words) : words);
        }

        /// <summary>A thing as a person names it: the scope's chip text (<i>Objective: Hold the bridge</i>), else its key (<c>@Bridge</c>), else <c>#id</c>.</summary>
        public static string NameOf(EntityLink link, LiveRefScope? refs)
        {
            if (!link.Ref.IsNone && refs != null) return refs.ChipLabelOf(link.Ref);
            if (link.Spec.Form == RefForm.Key) return "@" + link.Spec.KeyText;
            return link.Ref.IsNone ? "(nothing)" : "#" + link.Ref.NetworkId;
        }

        private static string Capital(string s) => s.Length == 0 || char.IsUpper(s[0]) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static string Plural(string words)
            => words.StartsWith("is ", StringComparison.Ordinal) ? "are " + words.Substring(3)
             : words.StartsWith("has ", StringComparison.Ordinal) ? "have " + words.Substring(4)
             : words;

        // ── the cached form, for a per-frame caller (a collapsed node) ────────────────────────────────────────────────────

        private sealed class Cached { public long Stamp; public string Text = ""; }
        private static readonly ConditionalWeakTable<SearchPredicateDto, Cached> _cache = new();

        /// <summary>
        /// <see cref="Of(SearchPredicateDto?, LiveRefScope?)"/> for a caller that asks every frame: rebuilt only when <see cref="Stamp"/> moves (an edit in
        /// place), else the kept string — ⛔ no allocation on a steady frame (R-2084).
        /// </summary>
        public static string Summary(SearchPredicateDto dto)
        {
            long stamp = Stamp(dto);
            var c = _cache.GetValue(dto, static _ => new Cached { Stamp = long.MinValue });
            if (c.Stamp != stamp) { c.Text = Of(dto); c.Stamp = stamp; }
            return c.Text;
        }

        /// <summary>A cheap fingerprint of what the sentence reads (0 B): the kinds, the operators, the numbers, the links, the strings by reference.</summary>
        public static long Stamp(SearchPredicateDto? d)
        {
            unchecked
            {
                if (d is null) return 17;
                long h = RuntimeHelpers.GetHashCode(d.GetType());
                switch (d)
                {
                    case CompoundPredicateDto c:
                        h = h * 31 + (int)c.Operator;
                        for (int i = 0; i < c.Conditions.Count; i++) h = h * 31 + Stamp(c.Conditions[i]);
                        break;
                    case OnEntityPredicateDto o: h = h * 31 + o.Ref.GetHashCode(); h = h * 31 + o.Ref.Spec.GetHashCode(); h = h * 31 + Stamp(o.Predicate); break;
                    case CountMatchingPredicateDto cm: h = h * 31 + cm.Min; h = h * 31 + cm.Max; h = h * 31 + Stamp(cm.Predicate); break;
                    case ScenarioTimePredicateDto t: h = h * 31 + t.FromSeconds.GetHashCode(); h = h * 31 + t.ToSeconds.GetHashCode(); break;
                    case InputAbovePredicateDto ia: h = h * 31 + RuntimeHelpers.GetHashCode(ia.Input); h = h * 31 + ia.Threshold.GetHashCode(); break;
                    case InUnitPredicateDto u: h = h * 31 + u.Unit.GetHashCode(); h = h * 31 + (u.IncludeSelf ? 1 : 0); break;
                    case SpatialBoundingPredicateDto s: h = h * 31 + s.Bounds.GetHashCode(); break;
                    case PropertyMatchDto pm:
                        h = h * 31 + (pm.ComponentType == null ? 0 : RuntimeHelpers.GetHashCode(pm.ComponentType));
                        h = h * 31 + RuntimeHelpers.GetHashCode(pm.PropertyPath); h = h * 31 + (int)pm.Operator; h = h * 31 + Stamp(pm.Predicate);
                        break;
                    case NumericPredicateDto n: h = h * 31 + n.MinValue.GetHashCode(); h = h * 31 + n.MaxValue.GetHashCode(); break;
                    case StringPredicateDto s: h = h * 31 + RuntimeHelpers.GetHashCode(s.Substring); break;
                    default: h = h * 31 + RuntimeHelpers.GetHashCode(d); break;   // a debugging kind: by identity (its text is technical)
                }
                return h;
            }
        }

        // ── the technical summary — ⭐ BreakpointConditionSummarizer folded in (CE-1285 / CE-1323) ────────────────────────

        /// <summary>A debugging condition as a short technical line (the Data Breakpoint Manager's column): <c>Component: Health.Current &lt;= 20</c>.</summary>
        public static string Technical(SearchPredicateDto? dto) => dto switch
        {
            null                           => "(none)",
            PropertyMatchDto pm            => Property(pm),
            TransientEventPredicateDto te  => $"Event: {te.EventType?.Name ?? "?"}",
            BehaviorParamPredicateDto bp   => $"BParam: {bp.BehaviorId}",
            StructuralPredicateDto st      => $"Structural: {st.ModificationType}",
            SpatialBoundingPredicateDto    => "Spatial",
            LifecyclePredicateDto lc       => $"Lifecycle: {lc.IdentifierType}",
            TraceBufferScanPredicateDto tr => $"Trace[0x{tr.OpCode:X2}]",
            CompoundPredicateDto cp        => $"Compound[{cp.Operator}]({cp.Conditions.Count}): " + JoinTechnical(cp),
            BlueprintVariablePredicateDto bv => $"Blueprint: {bv.TargetBlueprintAssetId.ToString()[..8]}...",
            BehaviourStepPredicateDto st   => Step(st),
            ExternalHitTagPredicateDto et  => et.Tag.StartsWith("btree:", StringComparison.Ordinal) ? "the node runs" : $"Tag: {et.Tag}",
            _                              => StructEdit.Core.Attributes.DisplayNames.Of(dto.GetType()),   // the kind's name (U-6), never the CLR name
        };

        private static string JoinTechnical(CompoundPredicateDto cp)
        {
            string sep = cp.Operator switch { LogicalOperator.And => " AND ", LogicalOperator.None => " NOR ", _ => " OR " };
            var sb = new StringBuilder();
            for (int i = 0; i < cp.Conditions.Count; i++) { if (i > 0) sb.Append(sep); sb.Append(Technical(cp.Conditions[i])); }
            return sb.ToString();
        }

        /// <summary>⭐ CE-1323 — a behaviour step as a sentence: "Withdrawing entered (squad-hsm)".</summary>
        public static string Step(BehaviourStepPredicateDto st)
        {
            string label = st.Label.Length > 0 ? st.Label : st.Kind switch
            {
                BehaviourStepKind.TransitionFires => $"{(string.IsNullOrEmpty(st.From) ? "any state" : st.From)} -> {st.To}"
                                                     + (string.IsNullOrEmpty(st.Event) ? "" : $" on {st.Event}"),
                BehaviourStepKind.EventHandled    => st.Event ?? "?",
                BehaviourStepKind.NodeRuns        => st.Node == Guid.Empty ? "?" : st.Node.ToString("N")[..8],
                _                                 => st.State ?? "?",
            };
            string what = st.Kind switch
            {
                BehaviourStepKind.NodeRuns        => st.Outcome switch
                {
                    BehaviourNodeOutcome.Running   => $"node {label} activates",
                    BehaviourNodeOutcome.Completes => $"node {label} completes",
                    BehaviourNodeOutcome.Aborted   => $"node {label} is interrupted",
                    _                              => $"node {label} runs",
                },
                BehaviourStepKind.StateEnter      => $"{label} entered",
                BehaviourStepKind.StateExit       => $"{label} left",
                BehaviourStepKind.InsideEnter     => $"anything inside {label}{(st.Region >= 0 ? $" (region {st.Region})" : "")} entered",
                BehaviourStepKind.TransitionFires => $"{label} fires",
                BehaviourStepKind.EventHandled    => $"event {label} handled",
                _                                 => label,
            };
            return st.Behaviour.Length > 0 ? $"{what} ({st.Behaviour})" : what;
        }

        /// <summary>"Component: Type.Path op value" — the field, never a raw infinite range.</summary>
        private static string Property(PropertyMatchDto pm)
        {
            string field = (pm.ComponentType?.Name ?? "?") + (string.IsNullOrEmpty(pm.PropertyPath) ? "" : "." + pm.PropertyPath);
            string what = pm.Operator switch
            {
                SearchOperator.Changed     => "changed",
                SearchOperator.Contains    => $"contains {Value(pm.Predicate)}",
                SearchOperator.StartsWith  => $"starts with {Value(pm.Predicate)}",
                SearchOperator.GreaterThan => $"> {Value(pm.Predicate)}",
                SearchOperator.LessThan    => $"< {Value(pm.Predicate)}",
                _                          => Value(pm.Predicate) is { Length: > 0 } v ? v : "",
            };
            return $"Component: {field}{(what.Length > 0 ? " " + what : "")}";
        }

        private static string Value(SearchPredicateDto? pred) => pred switch
        {
            NumericPredicateDto n  => Range(n.MinValue, n.MaxValue),
            StringPredicateDto s   => $"\"{s.Substring}\"",
            _                      => string.Empty,
        };

        /// <summary>A numeric range as people write it: <c>== 5</c>, <c>&gt;= 5</c>, <c>&lt;= 9</c>, <c>in [5, 9]</c>, or nothing when unbounded.</summary>
        private static string Range(double min, double max)
        {
            bool lo = min > double.MinValue / 2, hi = max < double.MaxValue / 2;
            if (lo && hi) return min == max ? $"== {min}" : $"in [{min}, {max}]";
            if (lo) return $">= {min}";
            if (hi) return $"<= {max}";
            return string.Empty;
        }
    }
}
