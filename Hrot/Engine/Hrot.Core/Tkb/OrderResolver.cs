using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.Core.Tkb
{
    /// <summary>How an order resolved for one entity.</summary>
    public enum OrderResolution : byte
    {
        /// <summary>A behaviour fulfils it: <see cref="OrderResolver.Result.Behaviour"/>.</summary>
        Resolved = 0,
        /// <summary>No class the entity belongs to has a behaviour for this order.</summary>
        NoBehaviour = 1,
        /// <summary>Two classes of the same specificity both fulfil it — a data error (rule O2: a tie is never guessed).</summary>
        Tie = 2,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2142</c> (D3, rule O2, R-273 / R-279 (behaviors)) — the RECEIVER decides how it fulfils an order: the behaviour for
    /// order <c>K</c> on an entity is <c>&lt;Class&gt;&lt;K&gt;</c> for the entity's most specific class (by its DIS type) that has
    /// one, then the broader ones. Nothing is mapped and nothing is guessed: two classes of equal specificity that both fulfil the
    /// order are a tie. One function, so the order lookup (TacticalIntentResolutionSystem) and the menu that offers orders (D4) can
    /// never disagree. 📄 docs/DESIGN_Orders_Params_Entity_Classes.md §2 (OrderResolver), §3.1, §8.
    /// </summary>
    public static class OrderResolver
    {
        public readonly record struct Result(OrderResolution Outcome, string Behaviour, string Detail);

        /// <summary>
        /// The behaviour that fulfils <paramref name="orderName"/> for an entity of type <paramref name="dis"/>, given which behaviour
        /// names exist (<paramref name="isBehaviour"/>).
        /// </summary>
        public static Result Resolve(EntityClassTable table, DISEntityType dis, string orderName, Func<string, bool> isBehaviour)
        {
            if (table is null) throw new ArgumentNullException(nameof(table));
            if (string.IsNullOrEmpty(orderName)) throw new ArgumentException("an order needs a name", nameof(orderName));
            var chain = table.Chain(dis);                      // general → specific
            for (int i = chain.Count - 1; i >= 0;)
            {
                int level = chain[i].Mask.Specificity;
                string? found = null, other = null;
                for (; i >= 0 && chain[i].Mask.Specificity == level; i--)
                {
                    string name = BehaviourName(chain[i].Name, orderName);
                    if (!isBehaviour(name)) continue;
                    if (found is null) found = name; else other ??= name;
                }
                if (other is not null)
                    return new Result(OrderResolution.Tie, string.Empty,
                        $"order '{orderName}' for DIS {dis}: '{found}' and '{other}' fulfil it at the same specificity");
                if (found is not null) return new Result(OrderResolution.Resolved, found, string.Empty);
            }
            return new Result(OrderResolution.NoBehaviour, string.Empty,
                $"order '{orderName}' for DIS {dis}: no behaviour named <Class>{orderName} for its classes ({Names(chain)})");
        }

        /// <summary>⭐ <c>CE-4350</c> — THE order-behaviour name: <c>&lt;Class&gt;&lt;Order&gt;</c> (R-278). <see cref="Resolve"/> builds it with this,
        /// <see cref="ClassOfBehaviour"/> splits it with the inverse, so the two can never disagree.</summary>
        public static string BehaviourName(string className, string orderName) => className + orderName;

        /// <summary>⭐ <c>CE-4350</c> — the inverse of <see cref="BehaviourName"/>: the class prefix of <paramref name="behaviour"/> when it is
        /// <c>&lt;Class&gt;&lt;orderName&gt;</c> and that class is in <paramref name="table"/>; else null.</summary>
        public static string? ClassOfBehaviour(EntityClassTable table, string behaviour, string orderName)
        {
            if (string.IsNullOrEmpty(orderName) || behaviour.Length <= orderName.Length
                || !behaviour.EndsWith(orderName, StringComparison.Ordinal)) return null;
            string prefix = behaviour.Substring(0, behaviour.Length - orderName.Length);
            return table.Find(prefix) is null ? null : prefix;
        }

        private static string Names(IReadOnlyList<EntityClass> chain)
        {
            if (chain.Count == 0) return "none";
            var parts = new string[chain.Count];
            for (int i = 0; i < chain.Count; i++) parts[i] = chain[i].Name;
            return string.Join(", ", parts);
        }
    }
}
