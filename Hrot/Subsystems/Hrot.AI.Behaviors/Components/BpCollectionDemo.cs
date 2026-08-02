using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.AI.Behaviors
{
    /// <summary>
    /// Component-access demo ECS component (CA-07a visual test target). A small blittable component
    /// carrying a fixed-capacity inline buffer (<see cref="Values"/>) plus its logical
    /// <see cref="Count"/>, attached to entities purely so the editor's <c>GetComponent</c> node has
    /// something concrete to discover a virtual COLLECTION off, via the curated
    /// <see cref="Brains.BpCollectionDemoOps"/> accessors (see <see cref="BlueprintCollectionAttribute"/>'s
    /// doc comment for the "why": raw <c>fixed</c>-array access stays off-graph, confined to that
    /// tiny helper). Mirrors <c>BpComponentDemo</c> (CA-01..CA-06's scalar-field demo) — this is the
    /// collection counterpart. No demo <c>.bp.json</c> yet (CA-07a) -- consumer nodes for the
    /// collection out-pin don't exist until CA-07b.
    /// </summary>
    [ComponentId(189)] // Hrot application-level block (160-199, HrotComponentIds); next free after 188 (BpComponentDemo).
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct BpCollectionDemo
    {
        /// <summary>Maximum number of elements <see cref="Values"/> can hold.</summary>
        public const int Capacity = 4;

        /// <summary>Number of currently valid entries in <see cref="Values"/> (0-<see cref="Capacity"/>).</summary>
        public int Count;

        /// <summary>Fixed-capacity inline buffer; only the first <see cref="Count"/> entries are valid.</summary>
        public fixed int Values[Capacity];
    }
}
