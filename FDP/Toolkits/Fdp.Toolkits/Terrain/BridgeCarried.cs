using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What a bridge engineer's brain has told its hull to do with its span (<see cref="BridgeCarried.Order"/>).</summary>
    public enum BridgeOrder : byte { None = 0, Lay = 1, Recover = 2 }

    /// <summary>⭐ <c>CE-4693</c> (BL-11) — why the engineer's last bridge order was refused (<see cref="BridgeCarried.Refusal"/>); <see cref="NotRefused"/> = not refused.
    /// The text said to whoever gave the order is <c>BridgeLaying.Say</c>.</summary>
    public enum BridgeRefusal : byte
    {
        /// <summary>Not refused. ⚠ Not <c>None</c>: the DDS IDL puts this enum's members in one scope with <see cref="BridgeOrder"/>'s.</summary>
        NotRefused = 0,
        /// <summary>No terrain is loaded.</summary>
        NoTerrain,
        /// <summary>The two banks are the same place.</summary>
        SamePlace,
        /// <summary>The near bank is in the water.</summary>
        NearBankInWater,
        /// <summary>The far bank is in the water.</summary>
        FarBankInWater,
        /// <summary>There is no water between the banks.</summary>
        NoWater,
        /// <summary>The water gap is wider than the engineer's span (<see cref="BridgeCarried.GapM"/>).</summary>
        GapTooWide,
        /// <summary>A deck end would stand in the water.</summary>
        DeckEndInWater,
        /// <summary>The engineer has no span on board to lay.</summary>
        NoSpanCarried,
        /// <summary>Recover: the target is not a laid bridge this engineer can carry.</summary>
        NotItsBridge,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4693</c> (BL-S3, BL-10, BL-17; 📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §12) — a BRIDGE ENGINEER's span: how many it carries and what its brain
    /// has told it to do with one. Written ONLY by the engineer's brain on the engineer itself (R-4212): the order (lay between two banks, recover a laid bridge) and the
    /// count, which it updates when it SEES the result. ⭐ The work is the mover's: <c>BridgeWorkSystem</c> (the node that moves the hull) holds the order for its seconds
    /// and creates / destroys the bridge obstacle — it never writes this. Unmanaged, recorded; Brain group, replicated (<c>dtBridgeCarried</c>).
    /// A renderer reads it: carried = <see cref="Count"/> &gt; 0 with no order; laying / recovering = <see cref="Order"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BridgeCarried)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct BridgeCarried
    {
        /// <summary>Spans on board.</summary>
        public byte Count;
        /// <summary>What the brain has told the hull to do.</summary>
        public BridgeOrder Order;
        /// <summary>The span's TKB type (a walkable-deck static obstacle).</summary>
        public long BridgeType;
        /// <summary>Lay: the near bank (where the hull stands) and the far bank — the deck runs between them.</summary>
        public WorldPoint NearBank;
        public WorldPoint FarBank;
        /// <summary>Recover: the laid bridge (its network id).</summary>
        public Fdp.Toolkit.Replication.EntityRef Target;
        /// <summary>⭐ BL-11 — why the last order was refused (<see cref="BridgeRefusal.NotRefused"/> while it is not), and the water gap it measured (m).</summary>
        public BridgeRefusal Refusal;
        public float GapM;
    }
}
