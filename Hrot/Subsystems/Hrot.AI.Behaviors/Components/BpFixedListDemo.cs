using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.AI.Behaviors
{
    /// <summary>
    /// Fixed Collections demo/reference ECS component (FC-0) -- the <c>[InlineArray]</c>-backed
    /// counterpart of <see cref="BpCollectionDemo"/> (whose buffer is a raw C# <c>fixed</c> array).
    /// Exists because the two idioms have DIFFERENT write hazards: the C# 12 inline-array
    /// defensive-copy trap (see <c>EntityRepository.GetComponentRW</c>'s "InlineArray Mutation
    /// Trap" doc) only exists for <c>[InlineArray]</c> fields, so the FC-0 write round-trip gate
    /// must exercise THIS shape -- a <c>fixed</c>-buffer demo would pass even with a broken write
    /// pattern (Q#20 review G7).
    ///
    /// <para>
    /// <c>[BlueprintWritable]</c> is gate 1 of collection writability; gate 2 is the presence of
    /// the curated write accessors in <see cref="Brains.BpFixedListDemoOps"/> (Q#20-A amendment).
    /// Canonical storage shape per the Fixed Collections design: inline buffer + sibling logical
    /// <see cref="Count"/> (capacity != count), tail slots &gt;= <see cref="Count"/> always
    /// <c>default</c> (the tail-always-default invariant, maintained by the accessors).
    /// </para>
    /// </summary>
    [ComponentId(191)] // Hrot application-level block (160-199, HrotComponentIds); next free after 190 (BpManagedCollectionDemo).
    [StructLayout(LayoutKind.Sequential)]
    [BlueprintWritable]
    public struct BpFixedListDemo
    {
        /// <summary>Maximum number of elements <see cref="Items"/> can hold.</summary>
        public const int Capacity = 4;

        /// <summary>Inline fixed-capacity element storage for <see cref="Items"/>.</summary>
        [InlineArray(Capacity)]
        public struct Buffer
        {
            private int _e0;
        }

        /// <summary>Number of currently valid entries in <see cref="Items"/> (0-<see cref="Capacity"/>).</summary>
        public int Count;

        /// <summary>Fixed-capacity inline buffer; only the first <see cref="Count"/> entries are valid, and entries at index &gt;= <see cref="Count"/> are always <c>default</c>.</summary>
        public Buffer Items;
    }
}
