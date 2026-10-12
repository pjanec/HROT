using System;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2136</c> (B4a, docs/DESIGN_Orders_Params_Entity_Classes.md §12) — marks a type as a PARAMS DTO even when no behaviour
    /// declares it yet: the params DTO asset's generator puts it on every generated DTO, so <see cref="ParamsDtoRegistry"/> lists it
    /// (and a behaviour can then name it). A hand-written DTO may carry it too; <c>[Behavior(Params = …)]</c> and <c>[Order]</c>
    /// already list theirs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ParamsDtoAttribute : Attribute
    {
        /// <summary>The DTO's default as one JSON object (only the fields that have one) — what a behaviour's
        /// <c>ParamsDefaultJson</c> can be seeded from. <c>default(T)</c> cannot carry it (R-237).</summary>
        public string? DefaultJson { get; set; }
    }
}
