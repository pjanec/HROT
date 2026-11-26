using System;

namespace CarKinem.Road
{
    /// <summary>
    /// ⭐ T-7b (CE-4423, docs/DESIGN_Terrain_Authoring.md §18k) — a copy of a road network without some of its segments (a fallen bridge's). Nodes and the other segments keep their order, so the base's indices are
    /// unchanged (a bridge's segments were APPENDED last, T-7a); a segment AFTER a dropped one moves down by the number dropped before it. Pure: the source is not touched.
    /// </summary>
    public static class RoadNetworkEdit
    {
        /// <summary>The network of <paramref name="source"/> minus the segments <paramref name="drop"/> says yes to; the caller owns (disposes, or publishes to a <see cref="RoadNetworkHolder"/>) the result.</summary>
        public static RoadNetworkBlob Without(in RoadNetworkBlob source, Func<int, bool> drop) => Edited(in source, drop, null);

        /// <summary>
        /// ⭐ <c>CE-4692</c> BL-7 (📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §3) — <paramref name="source"/> minus the segments <paramref name="drop"/> says yes to, PLUS one straight segment per
        /// <paramref name="added"/> entry (a laid bridge's road: two of the source's node indices), appended LAST — so the base's segments keep their order. The caller owns the result.
        /// </summary>
        public static RoadNetworkBlob Edited(in RoadNetworkBlob source, Func<int, bool> drop, System.Collections.Generic.IReadOnlyList<(int NodeA, int NodeB, float SpeedLimit, float LaneWidth)>? added)
        {
            var b = new RoadNetworkBuilder();
            for (int i = 0; i < source.Nodes.Length; i++) b.AddNode(source.Nodes[i].Position, source.Nodes[i].AboveGround);
            for (int i = 0; i < source.Segments.Length; i++)
            {
                if (drop(i)) continue;
                ref readonly var s = ref source.Segments[i];
                b.AddSegment(s.P0, s.T0, s.P1, s.T1, s.SpeedLimit, s.LaneWidth, s.LaneCount, s.StartNodeIndex, s.EndNodeIndex);
            }
            if (added != null)
                foreach (var (na, nb, speed, width) in added)
                {
                    if ((uint)na >= (uint)source.Nodes.Length || (uint)nb >= (uint)source.Nodes.Length || na == nb) continue;
                    var p0 = source.Nodes[na].Position; var p1 = source.Nodes[nb].Position; var d = p1 - p0;
                    b.AddSegment(p0, d, p1, d, speed, width, 1, na, nb);   // one lane, both ways (as the authored bridge's road, BridgeRoads.RoadsJson)
                }
            return b.Build(source.CellSize, source.Width, source.Height);
        }
    }
}
