using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ <c>CE-4693</c> (BL-S3 / BL-S4; 📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §12, BL-19) — an ENGINEER that lays bridges: which bridge it carries
    /// (a static-obstacle type of the walkable-deck kind, terrain's 8825 / 8826), how many spans, the longest water gap it bridges, and how long laying and
    /// recovering take. ⭐ The engineer's capability lives here; the span's own size stays the bridge type's <see cref="StaticObstacleDto"/> (two owners, no copy).
    /// <see cref="Source"/> names where the figures come from (Q-6) — "estimate" where no open source was found.
    /// </summary>
    [TkbDescriptor("Engineering.BridgeLayer")]
    public record BridgeLayerDto
    {
        [Description("The bridge it lays: a static-obstacle TKB type of the walkable-deck kind.")]
        public long BridgeType { get; init; }

        [Description("How many spans it carries when it starts.")]
        public int Carried { get; init; } = 1;

        [Description("The longest water gap (bank to bank, m) it bridges; a wider gap is refused.")]
        public float SpanMaxM { get; init; }

        [Description("Seconds at the near bank to lay the bridge.")]
        public float BuildSeconds { get; init; }

        [Description("Seconds at a deck end to recover the bridge.")]
        public float RecoverSeconds { get; init; }

        [Description("Where the figures come from (open sources), or 'estimate'.")]
        public string Source { get; init; } = "";
    }
}
