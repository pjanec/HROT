using System;
using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// FC-0 -- the REFERENCE curated accessor surface for <see cref="BpFixedListDemo"/>'s "Items"
    /// collection: the read pair (CA-07a convention, unchanged) plus the full write set per the
    /// Q#20 "G1 resolution" convention. This class is the template the FC-1b source generator must
    /// reproduce, and the FC-0 InlineArray write round-trip test gates it.
    ///
    /// <para>
    /// <b>The three load-bearing rules (do not "simplify" away):</b>
    /// <list type="number">
    ///   <item><b><c>Span&lt;T&gt;</c> write-through.</b> Every element access goes through
    ///   <c>(Span&lt;int&gt;)c.Items</c> / <c>(ReadOnlySpan&lt;int&gt;)c.Items</c> -- never the
    ///   inline-array indexer on a ref-chain (<c>c.Items[i] = v</c>), per
    ///   <c>EntityRepository.GetComponentRW</c>'s InlineArray Mutation Trap doc.</item>
    ///   <item><b>Tail-always-default invariant (G6).</b> Slots <c>&gt;= Count</c> are always
    ///   <c>default</c>: <c>RemoveAt</c>/<c>Clear</c>/<c>Resize</c>-shrink zero vacated slots at
    ///   mutation time; grow therefore NEVER fills.</item>
    ///   <item><b>Defensive Count clamp (List-Variables review F2, applied to all homes).</b> Ops
    ///   compute an effective length clamped to <c>[0, Capacity]</c> so a garbage <c>Count</c>
    ///   (corrupted/uninitialized memory) can never drive an out-of-bounds access.</item>
    /// </list>
    /// Mutators own <c>Count</c>; generated graph code and C# callers alike never touch the buffer
    /// or <c>Count</c> directly.
    /// </para>
    /// </summary>
    public static class BpFixedListDemoOps
    {
        // ---- read pair (CA-07a convention) --------------------------------------------------

        /// <summary>Number of currently valid entries (clamped to <c>[0, Capacity]</c>).</summary>
        [BlueprintCollection(typeof(BpFixedListDemo), "Items")]
        public static int Count(in BpFixedListDemo c) => Clamp(c.Count);

        /// <summary>The <paramref name="i"/>-th entry; caller guarantees <c>0 &lt;= i &lt; Count(in c)</c>.</summary>
        [BlueprintCollectionItem(typeof(BpFixedListDemo), "Items")]
        public static int Item(in BpFixedListDemo c, int i)
            => ((ReadOnlySpan<int>)c.Items)[i];

        // ---- write set (Q#20 G1 convention) -------------------------------------------------

        /// <summary>Append; <c>false</c> on full (never silent, never throws).</summary>
        [BlueprintCollectionWrite(typeof(BpFixedListDemo), "Items", BlueprintCollectionOp.Add)]
        public static bool Add(ref BpFixedListDemo c, int v)
        {
            int count = Clamp(c.Count);
            if (count >= BpFixedListDemo.Capacity) return false;
            ((Span<int>)c.Items)[count] = v;
            c.Count = count + 1;
            return true;
        }

        /// <summary>Overwrite within <c>[0, Count)</c>; never grows <c>Count</c>; <c>false</c> on out-of-range.</summary>
        [BlueprintCollectionWrite(typeof(BpFixedListDemo), "Items", BlueprintCollectionOp.SetAt)]
        public static bool SetAt(ref BpFixedListDemo c, int i, int v)
        {
            if ((uint)i >= (uint)Clamp(c.Count)) return false;
            ((Span<int>)c.Items)[i] = v;
            return true;
        }

        /// <summary>Insert, shifting the tail up; <c>i == Count</c> appends; <c>false</c> on full or <c>i &gt; Count</c>.</summary>
        [BlueprintCollectionWrite(typeof(BpFixedListDemo), "Items", BlueprintCollectionOp.InsertAt)]
        public static bool InsertAt(ref BpFixedListDemo c, int i, int v)
        {
            int count = Clamp(c.Count);
            if (count >= BpFixedListDemo.Capacity || (uint)i > (uint)count) return false;
            Span<int> s = c.Items;
            s[i..count].CopyTo(s[(i + 1)..]);   // overlapping-safe (memmove semantics)
            s[i] = v;
            c.Count = count + 1;
            return true;
        }

        /// <summary>Remove, shifting the tail down; zeroes the vacated slot (G6); <c>false</c> on out-of-range.</summary>
        [BlueprintCollectionWrite(typeof(BpFixedListDemo), "Items", BlueprintCollectionOp.RemoveAt)]
        public static bool RemoveAt(ref BpFixedListDemo c, int i)
        {
            int count = Clamp(c.Count);
            if ((uint)i >= (uint)count) return false;
            Span<int> s = c.Items;
            s[(i + 1)..count].CopyTo(s[i..]);
            s[count - 1] = default;             // G6: vacated slot re-zeroed
            c.Count = count - 1;
            return true;
        }

        /// <summary>Zero <c>[0, Count)</c> (G6) and set <c>Count = 0</c>. Cannot fail.</summary>
        [BlueprintCollectionWrite(typeof(BpFixedListDemo), "Items", BlueprintCollectionOp.Clear)]
        public static void Clear(ref BpFixedListDemo c)
        {
            ((Span<int>)c.Items)[..Clamp(c.Count)].Clear();
            c.Count = 0;
        }

        /// <summary>Set the logical length; shrink zeroes the dropped tail (G6), grow needs no fill; <c>false</c> when <paramref name="n"/> is out of <c>[0, Capacity]</c>.</summary>
        [BlueprintCollectionWrite(typeof(BpFixedListDemo), "Items", BlueprintCollectionOp.Resize)]
        public static bool Resize(ref BpFixedListDemo c, int n)
        {
            if ((uint)n > BpFixedListDemo.Capacity) return false;
            int count = Clamp(c.Count);
            if (n < count)
                ((Span<int>)c.Items)[n..count].Clear();   // G6: dropped tail re-zeroed
            c.Count = n;
            return true;
        }

        private static int Clamp(int count)
            => count < 0 ? 0 : count > BpFixedListDemo.Capacity ? BpFixedListDemo.Capacity : count;
    }
}
