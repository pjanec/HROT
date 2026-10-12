using System;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐⭐ <c>CE-2134</c> (B2, R-276/R-277; <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §5b) — a BEHAVIOUR declares its params
    /// DTO: the one type its JSON params are, published by <c>GET /behaviors</c> and the editor's forms. It sits ON THE BEHAVIOUR
    /// (its curated topology method, its curated resolver method, the intent mapper class that fulfils an intent id, or
    /// <c>[assembly: Behavior]</c> beside a host's own registration), never on the DTO — so one DTO can serve many behaviours
    /// (every <c>&lt;Class&gt;Move</c> shares <c>MoveOrder</c>). Replaces <c>[BehaviorContract]</c>, which sat on the DTO and named
    /// one behaviour.
    /// <para>⭐ Read by ONE scan, <see cref="ParamsDtoRegistry"/>; the curated generator checks it against the resolver's
    /// <c>TAuthored</c> (BEH002) and an identity-parsed <c>ParamsType</c> (BEH003).</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Assembly,
        Inherited = false, AllowMultiple = true)]
    public sealed class BehaviorAttribute : Attribute
    {
        public BehaviorAttribute(string name) => Name = name ?? throw new ArgumentNullException(nameof(name));

        /// <summary>The behaviour's REGISTRY name.</summary>
        public string Name { get; }

        /// <summary>The behaviour's params DTO (P1: exactly one; a behaviour that takes nothing names <see cref="NoParams"/>).</summary>
        public Type? Params { get; set; }

        /// <summary>The entity categories the behaviour suits (the mission pickers' filter) until the class table replaces it
        /// (CE-2138 / CE-2142).</summary>
        public BehaviorCategory Categories { get; set; }
    }
}
