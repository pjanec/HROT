using System;

namespace Fdp.Core
{
    /// <summary>
    /// The mutation verb a <see cref="BlueprintCollectionWriteAttribute"/>-marked curated write
    /// accessor implements over a fixed-capacity component collection (Fixed Collections, architect
    /// Q#20 — see <c>docs/blueprints/Architect_Question_20_Component_Collection_Write.md</c>
    /// §"G1 resolution" for the full convention).
    /// </summary>
    public enum BlueprintCollectionOp
    {
        /// <summary><c>bool Add(ref C c, Elem v)</c> — append; <c>false</c> on full.</summary>
        Add = 0,
        /// <summary><c>bool SetAt(ref C c, int i, Elem v)</c> — overwrite within <c>[0, Count)</c>; never grows <c>Count</c>; <c>false</c> on out-of-range.</summary>
        SetAt = 1,
        /// <summary><c>bool InsertAt(ref C c, int i, Elem v)</c> — shift tail up; <c>false</c> on full or <c>i &gt; Count</c>.</summary>
        InsertAt = 2,
        /// <summary><c>bool RemoveAt(ref C c, int i)</c> — shift tail down and ZERO the vacated slot (tail-always-default invariant); <c>false</c> on out-of-range.</summary>
        RemoveAt = 3,
        /// <summary><c>void Clear(ref C c)</c> — zero <c>[0, Count)</c> (tail-always-default invariant), <c>Count = 0</c>. Cannot fail.</summary>
        Clear = 4,
        /// <summary><c>bool Resize(ref C c, int n)</c> — set logical length; shrink ZEROES the dropped tail (tail-always-default invariant), grow needs no fill; <c>false</c> when <c>n</c> exceeds capacity.</summary>
        Resize = 5,
    }

    /// <summary>
    /// Marks a curated WRITE accessor static for a virtual collection exposed by an ECS component --
    /// the write-side sibling of the <see cref="BlueprintCollectionAttribute"/>/
    /// <see cref="BlueprintCollectionItemAttribute"/> read pair (same
    /// <see cref="ComponentType"/> + <see cref="Name"/> identify the same collection).
    ///
    /// <para>
    /// <b>Why (architect Q#20 / Q#5-C):</b> raw fixed/inline-array mutation stays OUT of generated
    /// graph code, confined to a tiny curated helper the emitter calls by baked FQN
    /// (<c>Ops.Add(ref __wc, v)</c> on the <c>GetComponentRW</c> ref) -- exactly mirroring the read
    /// emit. The <c>[InlineArray]</c> defensive-copy write hazard is neutralized INSIDE the accessor
    /// by the mandatory <c>Span&lt;T&gt;</c> write-through pattern
    /// (<c>((Span&lt;Elem&gt;)c.Buf)[i] = v</c> -- see <c>EntityRepository.GetComponentRW</c>'s
    /// "InlineArray Mutation Trap" doc), where the compiler can verify it via the FC-0 round-trip
    /// test. Accessors own <c>Count</c> maintenance and the tail-always-default invariant (slots
    /// <c>&gt;= Count</c> are always <c>default(Elem)</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Curation semantics:</b> the PRESENCE of write accessors is the per-field mutability opt-in
    /// (Q#20-A amendment): a <c>[BlueprintWritable]</c> component whose collection ships only the
    /// read pair stays read-only from blueprints, and a PARTIAL write set is legal -- the editor
    /// palette offers exactly the ops that exist. Both gates apply: the component must be
    /// <c>[BlueprintWritable]</c> AND the op's accessor must exist.
    /// </para>
    ///
    /// <para>
    /// <b>Contract:</b> the attributed method MUST be <c>public static</c>, take <c>ref TComp</c> as
    /// its first parameter (matching <see cref="ComponentType"/>), the remaining parameters per
    /// <see cref="BlueprintCollectionOp"/>'s doc, and return <c>bool</c> (<see
    /// cref="BlueprintCollectionOp.Clear"/>: <c>void</c>) -- otherwise the accessor is silently
    /// ignored by discovery. Reference implementation: <c>BpFixedListDemoOps</c>
    /// (Hrot.AI.Behaviors).
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class BlueprintCollectionWriteAttribute : Attribute
    {
        /// <summary>The ECS component struct this collection lives on. Must match the read pair's <see cref="BlueprintCollectionAttribute.ComponentType"/>.</summary>
        public Type ComponentType { get; }

        /// <summary>Logical collection name -- must match the read pair's <see cref="BlueprintCollectionAttribute.Name"/>.</summary>
        public string Name { get; }

        /// <summary>The mutation verb this accessor implements (drives the pinned signature contract).</summary>
        public BlueprintCollectionOp Op { get; }

        public BlueprintCollectionWriteAttribute(Type componentType, string name, BlueprintCollectionOp op)
        {
            ComponentType = componentType;
            Name = name;
            Op = op;
        }
    }
}
