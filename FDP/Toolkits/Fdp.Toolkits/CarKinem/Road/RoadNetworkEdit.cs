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
        public static RoadNetworkBlob Without(in RoadNetworkBlob source, Func<int, bool> drop)
        {
            var b = new RoadNetworkBuilder();
            for (int i = 0; i < source.Nodes.Length; i++) b.AddNode(source.Nodes[i].Position, source.Nodes[i].AboveGround);
            for (int i = 0; i < source.Segments.Length; i++)
            {
                if (drop(i)) continue;
                ref readonly var s = ref source.Segments[i];
                b.AddSegment(s.P0, s.T0, s.P1, s.T1, s.SpeedLimit, s.LaneWidth, s.LaneCount, s.StartNodeIndex, s.EndNodeIndex);
            }
            return b.Build(source.CellSize, source.Width, source.Height);
        }
    }
}
