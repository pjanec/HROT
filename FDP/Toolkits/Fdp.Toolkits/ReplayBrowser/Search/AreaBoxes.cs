using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐ S0 item 5 (<c>DESIGN_Scenario_Rules</c> §9.8.0, SR-134) — the Replay Browser's spatial search area is an <see cref="AreaLink"/>
    /// (ONE area type, ONE containment: <see cref="AreaLink.Contains"/>). The search panel's map tool still DRAGS an axis-aligned box
    /// (<see cref="BoundingBox2D"/>); this is the one conversion between the two, so the picker and the editor draw what the predicate tests.
    /// </summary>
    public static class AreaBoxes
    {
        /// <summary>The axis-aligned rectangle covering <paramref name="box"/> (heading north, so width = X extent, length = Y extent).</summary>
        public static AreaLink FromBox(BoundingBox2D box)
            => AreaLink.Rectangle(new Vector3((box.Min.X + box.Max.X) * 0.5f, (box.Min.Y + box.Max.Y) * 0.5f, 0f),
                                  box.Max.X - box.Min.X, box.Max.Y - box.Min.Y, default);

        /// <summary>
        /// The axis-aligned box an area covers, for drawing it: a rectangle's extent (heading ignored — a turned rectangle is covered by
        /// its axis-aligned hull is NOT computed here, so this is exact only for heading 0 / 180), a circle's bounding square.
        /// <see langword="null"/> for an area ENTITY (no shape in the value).
        /// </summary>
        public static BoundingBox2D? ToBox(AreaLink area)
        {
            if (!area.IsShape) return null;
            var c = new Vector2(area.Center.X, area.Center.Y);
            var half = area.Spec.Form == RefForm.Circle ? new Vector2(area.Size.X, area.Size.X) : area.Size * 0.5f;
            return new BoundingBox2D { Min = c - half, Max = c + half };
        }
    }
}
