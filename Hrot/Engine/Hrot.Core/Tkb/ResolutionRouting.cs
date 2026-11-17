using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-8000</c> (CE-4616 §23.2; SV-2, B-1, RV-7; docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md) — THE
    /// (class, resolution, order) → behaviour TABLE. A unit that can switch keeps ONE resolution-neutral type and class (<c>InfantrySquad</c>);
    /// the resolution is an explicit INPUT here — ⛔ never a string concatenated into the behaviour name (SV-2).
    /// <list type="bullet">
    /// <item><see cref="AggregateResolution.Physical"/> (and unset) ⇒ <see cref="OrderResolver.Resolve"/> — today's physical rule, untouched; ⭐ <c>X-2</c>
    /// X2-C2 (R-8356, R-8302): only where that rule finds NOTHING, a PHYSICAL row for the entity's classes is the FALLBACK (a squad's Defend ⇒
    /// <c>SquadDefend</c>, F-X2-2) — so nothing that resolves today changes.</item>
    /// <item>any other resolution ⇒ a ROW for the entity's most specific class that has one (the class chain, specific → general, as
    /// <see cref="OrderResolver"/> walks it), ⭐ the SOP included (order <see cref="SopOrder"/>, RV-7). No row ⇒
    /// <see cref="OrderResolution.NoBehaviour"/> — the order is REFUSED (B-8: the receiver then reports TaskFailed). ⭐ A Fusion unit with no
    /// Fusion row falls back to the physical rule (X-1: the FusionSquad plays the squad's plays with its stand-in soldier).</item>
    /// </list>
    /// One function for the order lookup, the menu and the fit check, so they never disagree (the OrderResolver principle, D3).
    /// </summary>
    public sealed class ResolutionRoutingTable
    {
        /// <summary>The order name of the standing-orders row (RV-7: the SOP is resolved by the SAME table).</summary>
        public const string SopOrder = "Sop";

        /// <summary>One row: <paramref name="Class"/> (an <see cref="EntityClassTable"/> class name) at <paramref name="Resolution"/> fulfils
        /// <paramref name="Order"/> with <paramref name="Behaviour"/>.</summary>
        public readonly record struct Row(string Class, AggregateResolution Resolution, string Order, string Behaviour);

        private readonly Dictionary<(string, AggregateResolution, string), string> _rows = new();
        private readonly List<Row> _list = new();

        public ResolutionRoutingTable(IEnumerable<Row> rows)
        {
            foreach (var r in rows)
            {
                if (string.IsNullOrEmpty(r.Class) || string.IsNullOrEmpty(r.Order) || string.IsNullOrEmpty(r.Behaviour))
                    throw new ArgumentException($"routing row {r} needs a class, an order and a behaviour");
                if (r.Resolution is AggregateResolution.Unset)
                    throw new ArgumentException($"routing row {r}: name the resolution (Physical rows are OrderResolver's FALLBACK, X2-C2)");
                if (!_rows.TryAdd((r.Class, r.Resolution, r.Order), r.Behaviour))
                    throw new ArgumentException($"routing row {r} duplicates ({r.Class}, {r.Resolution}, {r.Order})");
                _list.Add(r);
            }
        }

        /// <summary>The rows, in declaration order (the editor's view; the contract NOTE lists them).</summary>
        public IReadOnlyList<Row> Rows => _list;

        /// <summary>The behaviour names of the generic stochastic plays (B-1) — ONE per order kind for every echelon.</summary>
        public static class Plays
        {
            public const string Move = "StochasticUnitMove";
            public const string Defend = "StochasticUnitDefend";
            public const string Attack = "StochasticUnitAttack";
            public const string BreakContact = "StochasticUnitBreakContact";
            public const string Sop = "StochasticUnitSop";
            /// <summary>⭐ <c>X-2</c> X2-E (R-8358) — the stochastic SOP's own hold (ST-2): no order row routes to it, but it IS a stochastic play,
            /// so the catalogue (<see cref="IsStochasticPlay"/>) is complete.</summary>
            public const string Hold = "StochasticUnitHold";
        }

        /// <summary>⭐ <c>X-2</c> X2-E (R-8358) — is <paramref name="behaviour"/> one of the generic stochastic plays (<see cref="Plays"/>)? A unit
        /// whose resolution and whose running play disagree switched under it (the reconciler's fit test).</summary>
        public static bool IsStochasticPlay(string? behaviour) => behaviour is Plays.Move or Plays.Defend or Plays.Attack or Plays.BreakContact
                                                                               or Plays.Sop or Plays.Hold;

        /// <summary>The names of every play <see cref="IsStochasticPlay"/> knows (the reconciler hashes them once).</summary>
        public static IReadOnlyList<string> StochasticPlays { get; } = new[] { Plays.Move, Plays.Defend, Plays.Attack, Plays.BreakContact, Plays.Sop, Plays.Hold };

        /// <summary>
        /// ⭐ The default table (B-1, B-6, B-8): every <c>Unit</c> at <see cref="AggregateResolution.Stochastic"/> — Move / March / Dash ⇒
        /// Move; Defend / Hold ⇒ Defend; Attack / ClearBuilding / Bound / FireAndMovement / SupportByFire ⇒ Attack (SupportByFire = without
        /// closing — the play reads the order); BreakContact ⇒ BreakContact; the SOP ⇒ <see cref="Plays.Sop"/>. ⛔ Strike, ObserveFire, Recover
        /// have NO row ⇒ refused (B-6, B-8); Embark / Dismount are the posture (S-9), added with ST-3's plays.
        /// </summary>
        public static ResolutionRoutingTable Default { get; } = new(DefaultRows());

        private static IEnumerable<Row> DefaultRows()
        {
            const string unit = "Unit";
            const AggregateResolution s = AggregateResolution.Stochastic;
            foreach (var o in new[] { "Move", "March", "Dash" }) yield return new Row(unit, s, o, Plays.Move);
            foreach (var o in new[] { "Defend", "Hold" }) yield return new Row(unit, s, o, Plays.Defend);
            foreach (var o in new[] { "Attack", "ClearBuilding", "Bound", "FireAndMovement", "SupportByFire" }) yield return new Row(unit, s, o, Plays.Attack);
            yield return new Row(unit, s, "BreakContact", Plays.BreakContact);
            yield return new Row(unit, s, SopOrder, Plays.Sop);
            // ⭐ X-2 X2-C2 (R-8356, R-8302 as approved) — the PHYSICAL FALLBACK rows (consulted only when OrderResolver's <Class><Order> rule
            //   finds nothing): a squad has no InfantrySquadDefend / InfantrySquadBreakContact, so an Order.Defend to a physical squad was refused
            //   (F-X2-2). Built from the plays' Fulfils + the echelon class; a rail refuses a row the name rule already resolves.
            const AggregateResolution p = AggregateResolution.Physical;
            yield return new Row("InfantrySquad", p, "Defend", "SquadDefend");
            yield return new Row("InfantrySquad", p, "BreakContact", "BreakContact");
        }

        /// <summary>The row for exactly (<paramref name="className"/>, <paramref name="resolution"/>, <paramref name="order"/>), else null.</summary>
        public string? Find(string className, AggregateResolution resolution, string order)
            => _rows.TryGetValue((className, resolution, order), out var b) ? b : null;

        /// <summary>
        /// The behaviour that fulfils <paramref name="orderName"/> for an entity of type <paramref name="dis"/> simulated at
        /// <paramref name="resolution"/>. <paramref name="isBehaviour"/> says which behaviour names exist (a row naming a behaviour not built yet
        /// resolves to <see cref="OrderResolution.NoBehaviour"/>, never to a missing name).
        /// </summary>
        public OrderResolver.Result Resolve(EntityClassTable classes, DISEntityType dis, AggregateResolution resolution, string orderName,
                                            Func<string, bool> isBehaviour)
        {
            if (classes is null) throw new ArgumentNullException(nameof(classes));
            if (string.IsNullOrEmpty(orderName)) throw new ArgumentException("an order needs a name", nameof(orderName));
            if (resolution is AggregateResolution.Unset or AggregateResolution.Physical)
            {
                var named = OrderResolver.Resolve(classes, dis, orderName, isBehaviour);
                if (named.Outcome != OrderResolution.NoBehaviour) return named;
                var fallback = RowFor(classes, dis, AggregateResolution.Physical, orderName, isBehaviour);   // ⭐ X2-C2 — only where the name rule finds nothing
                return fallback.Outcome == OrderResolution.NoBehaviour ? named : fallback;
            }
            var r = RowFor(classes, dis, resolution, orderName, isBehaviour);
            if (r.Outcome != OrderResolution.NoBehaviour) return r;
            if (resolution == AggregateResolution.Fusion)
                return OrderResolver.Resolve(classes, dis, orderName, isBehaviour);
            return r;
        }

        /// <summary>The most specific class's row at <paramref name="resolution"/> (the class chain walked specific → general, a tie at one
        /// specificity reported), else <see cref="OrderResolution.NoBehaviour"/>.</summary>
        private OrderResolver.Result RowFor(EntityClassTable classes, DISEntityType dis, AggregateResolution resolution, string orderName, Func<string, bool> isBehaviour)
        {
            var chain = classes.Chain(dis);                    // general → specific
            for (int i = chain.Count - 1; i >= 0;)
            {
                int level = chain[i].Mask.Specificity;
                string? found = null, other = null;
                for (; i >= 0 && chain[i].Mask.Specificity == level; i--)
                {
                    var b = Find(chain[i].Name, resolution, orderName);
                    if (b is null || !isBehaviour(b)) continue;
                    if (found is null) found = b; else if (b != found) other ??= b;
                }
                if (other is not null)
                    return new OrderResolver.Result(OrderResolution.Tie, string.Empty,
                        $"order '{orderName}' at {resolution} for DIS {dis}: '{found}' and '{other}' fulfil it at the same specificity");
                if (found is not null) return new OrderResolver.Result(OrderResolution.Resolved, found, string.Empty);
            }
            return new OrderResolver.Result(OrderResolution.NoBehaviour, string.Empty,
                $"order '{orderName}' at {resolution} for DIS {dis}: no routing row for its classes — refused");
        }
    }
}
