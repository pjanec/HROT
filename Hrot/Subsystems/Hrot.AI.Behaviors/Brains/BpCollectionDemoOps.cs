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
    }
}
