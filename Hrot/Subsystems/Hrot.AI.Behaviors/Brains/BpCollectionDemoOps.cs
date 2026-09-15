using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// CA-07a demo -- the curated accessor surface for <see cref="BpCollectionDemo"/>'s "Values"
    /// virtual collection, giving the editor's component-collection reflector
    /// (<c>ComponentFieldReflector.TryReflectCollections</c>) a concrete <c>[BlueprintCollection]</c>/
    /// <c>[BlueprintCollectionItem]</c> pair to discover. Mirrors <see cref="UnitRosterOps"/> exactly
    /// (same "keep the unsafe fixed-array access off-graph" shape, architect Q#5-C) -- this is a
    /// demo-only pairing, not itself consumed by any node yet (CA-07a bakes the collection metadata
    /// onto <c>GetComponentNode.Fields</c>; the consumer that reads element-N arrives in CA-07b).
    /// </summary>
    public static class BpCollectionDemoOps
    {
        /// <summary>Number of currently valid entries in <paramref name="c"/>'s <c>Values</c> buffer (0-4).</summary>
        [BlueprintCollection(typeof(BpCollectionDemo), "Values")]
        public static int Count(in BpCollectionDemo c) => c.Count;

        /// <summary>
        /// The <paramref name="i"/>-th entry of <paramref name="c"/>'s <c>Values</c> buffer. Caller
        /// (the eventual CA-07b consumer) is expected to guarantee <c>0 &lt;= i &lt; Count(in c)</c>.
        /// </summary>
        [BlueprintCollectionItem(typeof(BpCollectionDemo), "Values")]
        public static int Item(in BpCollectionDemo c, int i)
        {
            unsafe { return c.Values[i]; }
        }

        // ---- write set (FC-0, Q#20 G1 convention -- the raw-`fixed`-buffer idiom) -----------
        // The SECOND reference idiom next to BpFixedListDemoOps' [InlineArray] one (Q#20 review
        // G7: both buffer idioms exist in the wild, e.g. UnitRoster). `fixed` buffers have NO
        // inline-array defensive-copy trap -- direct indexing on the `ref` receiver mutates in
        // place (C# 7.3 movable-fixed-buffer indexing) -- but the contract is identical: pinned
        // signatures, mutators own Count, tail-always-default invariant (G6), defensive Count
        // clamp (F2). NOTE: BpCollectionDemo is deliberately NOT [BlueprintWritable] -- these
        // accessors also serve as the gate-1-vs-gate-2 discovery test case (write accessors
        // present, component-level gate absent => still not writable from blueprints).

        /// <summary>Append; <c>false</c> on full.</summary>
        [BlueprintCollectionWrite(typeof(BpCollectionDemo), "Values", BlueprintCollectionOp.Add)]
        public static bool Add(ref BpCollectionDemo c, int v)
        {
            int count = Clamp(c.Count);
            if (count >= BpCollectionDemo.Capacity) return false;
            unsafe { c.Values[count] = v; }
            c.Count = count + 1;
            return true;
        }

        /// <summary>Overwrite within <c>[0, Count)</c>; never grows <c>Count</c>; <c>false</c> on out-of-range.</summary>
        [BlueprintCollectionWrite(typeof(BpCollectionDemo), "Values", BlueprintCollectionOp.SetAt)]
        public static bool SetAt(ref BpCollectionDemo c, int i, int v)
        {
            if ((uint)i >= (uint)Clamp(c.Count)) return false;
            unsafe { c.Values[i] = v; }
            return true;
        }

        /// <summary>Insert, shifting the tail up; <c>i == Count</c> appends; <c>false</c> on full or <c>i &gt; Count</c>.</summary>
        [BlueprintCollectionWrite(typeof(BpCollectionDemo), "Values", BlueprintCollectionOp.InsertAt)]
        public static bool InsertAt(ref BpCollectionDemo c, int i, int v)
        {
            int count = Clamp(c.Count);
            if (count >= BpCollectionDemo.Capacity || (uint)i > (uint)count) return false;
            unsafe
            {
                for (int j = count; j > i; j--) c.Values[j] = c.Values[j - 1];
                c.Values[i] = v;
            }
            c.Count = count + 1;
            return true;
        }

        /// <summary>Remove, shifting the tail down; zeroes the vacated slot (G6); <c>false</c> on out-of-range.</summary>
        [BlueprintCollectionWrite(typeof(BpCollectionDemo), "Values", BlueprintCollectionOp.RemoveAt)]
        public static bool RemoveAt(ref BpCollectionDemo c, int i)
        {
            int count = Clamp(c.Count);
            if ((uint)i >= (uint)count) return false;
            unsafe
            {
                for (int j = i; j < count - 1; j++) c.Values[j] = c.Values[j + 1];
                c.Values[count - 1] = default;   // G6: vacated slot re-zeroed
            }
            c.Count = count - 1;
            return true;
        }

        /// <summary>Zero <c>[0, Count)</c> (G6) and set <c>Count = 0</c>. Cannot fail.</summary>
        [BlueprintCollectionWrite(typeof(BpCollectionDemo), "Values", BlueprintCollectionOp.Clear)]
        public static void Clear(ref BpCollectionDemo c)
        {
            int count = Clamp(c.Count);
            unsafe { for (int j = 0; j < count; j++) c.Values[j] = default; }
            c.Count = 0;
        }

        /// <summary>Set the logical length; shrink zeroes the dropped tail (G6), grow needs no fill; <c>false</c> out of <c>[0, Capacity]</c>.</summary>
        [BlueprintCollectionWrite(typeof(BpCollectionDemo), "Values", BlueprintCollectionOp.Resize)]
        public static bool Resize(ref BpCollectionDemo c, int n)
        {
            if ((uint)n > BpCollectionDemo.Capacity) return false;
            int count = Clamp(c.Count);
            unsafe { for (int j = n; j < count; j++) c.Values[j] = default; }   // G6
            c.Count = n;
            return true;
        }

        private static int Clamp(int count)
            => count < 0 ? 0 : count > BpCollectionDemo.Capacity ? BpCollectionDemo.Capacity : count;
    }
}
