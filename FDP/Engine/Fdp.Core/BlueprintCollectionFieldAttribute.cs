using System;

namespace Fdp.Core
{
    /// <summary>Access surface a generated collection ops class exposes (see <see cref="BlueprintCollectionFieldAttribute"/>).</summary>
    public enum CollectionAccess
    {
        /// <summary>Read pair + the write set selected by <see cref="BlueprintCollectionFieldAttribute.Ops"/> (default).</summary>
        ReadWrite = 0,
        /// <summary>Read pair only -- the collection stays read-only from blueprints (the per-field gate of Q#20-A, expressed declaratively).</summary>
        ReadOnly = 1,
    }

    /// <summary>Write-op subset a generated collection ops class exposes (see <see cref="BlueprintCollectionFieldAttribute"/>). Mirrors <see cref="BlueprintCollectionOp"/> as flags.</summary>
    [Flags]
    public enum CollectionOps
    {
        None     = 0,
        Add      = 1 << 0,
        SetAt    = 1 << 1,
        InsertAt = 1 << 2,
        RemoveAt = 1 << 3,
        Clear    = 1 << 4,
        Resize   = 1 << 5,
        All      = Add | SetAt | InsertAt | RemoveAt | Clear | Resize,
    }

    /// <summary>
    /// FC-1b (Fixed Collections, Q#20 "G1 resolution") -- marks a fixed-capacity
    /// <c>[InlineArray]</c> buffer field on an unmanaged ECS component so the
    /// <c>CollectionOpsGenerator</c> emits its curated accessor ops class
    /// (<c>{Component}{Field}Ops</c>: the <c>[BlueprintCollection]</c>/<c>[BlueprintCollectionItem]</c>
    /// read pair + the <c>[BlueprintCollectionWrite]</c> write set) from the FC-0 reference template
    /// (<c>BpFixedListDemoOps</c>) -- ONE attribute instead of ~60 lines of trap-prone hand-written
    /// code (Span write-through, Count maintenance, tail-always-default zeroing, defensive clamp).
    ///
    /// <para>
    /// <b>Deliberately opt-in per FIELD</b> (never auto-triggered off "any inline array on a
    /// writable component"): the logical count is a sibling field the generator cannot infer
    /// (capacity != count -- Q#17 reality check), auto-emitting mutators would delete the
    /// accessor-presence writability gate (Q#20-A amendment), and bespoke-semantics collections
    /// must keep hand-written ops. A HAND-WRITTEN accessor for the same (component, collection
    /// name) always WINS -- the generator emits nothing for that field (the escape hatch is
    /// automatic).
    /// </para>
    ///
    /// <para>
    /// Contract enforced by generator diagnostics: the field's type is an <c>[InlineArray(N)]</c>
    /// struct; <see cref="CountField"/> names a sibling <c>int</c> field; the element type is
    /// unmanaged; the component is an unmanaged struct (a managed/class component's collections are
    /// never element-writable -- Q#20-C). The collection's logical NAME is the field name.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// [ComponentId(...)]
    /// [BlueprintWritable]                       // gate 1 (component-level) stays explicit
    /// public struct PatrolPlan
    /// {
    ///     public const int Capacity = 8;
    ///     [InlineArray(Capacity)] public struct Buffer { private Entity _e0; }
    ///
    ///     public int Count;
    ///
    ///     [BlueprintCollectionField(nameof(Count))]
    ///     public Buffer Waypoints;              // => generated PatrolPlanWaypointsOps
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class BlueprintCollectionFieldAttribute : Attribute
    {
        /// <summary>Name of the sibling <c>int</c> field carrying the collection's logical length.</summary>
        public string CountField { get; }

        /// <summary>Read-only vs read-write surface. Default <see cref="CollectionAccess.ReadWrite"/>.</summary>
        public CollectionAccess Access { get; set; } = CollectionAccess.ReadWrite;

        /// <summary>The write-op subset to generate (ignored when <see cref="Access"/> is ReadOnly). Default <see cref="CollectionOps.All"/> -- a partial set is the per-op curation lever.</summary>
        public CollectionOps Ops { get; set; } = CollectionOps.All;

        public BlueprintCollectionFieldAttribute(string countField)
        {
            CountField = countField;
        }
    }
}
