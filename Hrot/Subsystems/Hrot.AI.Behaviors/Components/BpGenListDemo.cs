using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.AI.Behaviors
{
    /// <summary>
    /// FC-1b demo/proof ECS component -- the GENERATED-accessors counterpart of
    /// <see cref="BpFixedListDemo"/> (whose ops class is hand-written as the FC-0 reference
    /// template). The single <c>[BlueprintCollectionField]</c> attribute on <see cref="Items"/> is
    /// the ENTIRE authoring surface: <c>CollectionOpsGenerator</c> emits
    /// <c>BpGenListDemoItemsOps</c> (read pair + all six write ops) at compile time, and the
    /// FC-1b tests prove the generated class passes the exact same round-trip/invariant/overflow
    /// gates as the hand-written reference, and that the editor's reflector discovers it
    /// identically.
    /// </summary>
    [ComponentId(192)] // Hrot application-level block (160-199, HrotComponentIds); next free after 191 (BpFixedListDemo).
    [StructLayout(LayoutKind.Sequential)]
    [BlueprintWritable]
    public struct BpGenListDemo
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

        /// <summary>Fixed-capacity inline buffer; accessors are GENERATED (see the type doc comment).</summary>
        [BlueprintCollectionField(nameof(Count))]
        public Buffer Items;
    }
}
