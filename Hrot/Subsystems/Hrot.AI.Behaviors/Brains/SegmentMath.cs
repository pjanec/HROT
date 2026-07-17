using System;
using System.Numerics;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated, reflection-free firing-line segment math for blueprints. Bundles the
    /// clamp/cast/conditional-default arithmetic that has no visual-node expression (architect Q#6-A:
    /// non-trivial math stays in reviewable curated helpers; only plain arithmetic is a visual
    /// <c>BinaryOp</c>). Mirrors the <c>VectorOps</c>/<c>UnitRosterOps</c> off-graph-helper shape.
    /// </summary>
    public static class SegmentMath
    {
        /// <summary>
        /// Firing-line slot count for a start-&gt;end segment at the given tank spacing, clamped to
        /// [1, 16]. Reproduces the C# oracle
        /// <c>HillAttackCommanderNodes.Action_CalculateSegments</c>:
        /// <c>spacing = rawSpacing &gt; 0 ? rawSpacing : 30</c>, then
        /// <c>clamp(max(1, (int)(distance(start,end) / spacing)), 1, 16)</c>. A non-positive
        /// <paramref name="rawSpacing"/> falls back to the oracle's 30 m default (never divides by 0).
        /// </summary>
        public static int TotalSlots(float startX, float startY, float endX, float endY, float rawSpacing)
        {
            float segLen  = Vector2.Distance(new Vector2(startX, startY), new Vector2(endX, endY));
            float spacing = rawSpacing > 0f ? rawSpacing : 30f;
            int totalSlots = Math.Max(1, (int)(segLen / spacing));
            if (totalSlots > 16) totalSlots = 16;
            return totalSlots;
        }
    }
}
