using System;
using System.Linq;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Fdp.TestShared
{
    /// <summary>
    /// ⭐ CE-1203 (docs/DESIGN_Terrain_Combat_Tuning.md §5e FL-3) — the rail every map-layer row uses: what a gizmo DRAWS lies on its own
    /// layer, and with that layer hidden it draws nothing. Meta primitives (anchors, menus, the mask, panels) and pick targets are not
    /// drawings and are exempt (CE-1071 / CE-1160 — selection never depends on a toggle). Linked into each test project that has a
    /// gizmo suite (<c>Compile Include … Link="Shared\GizmoLayerProbe.cs"</c>), so the row's assertion sits in that gizmo's own suite.
    /// </summary>
    public static class GizmoLayerProbe
    {
        /// <summary>Emits through <paramref name="emit"/> twice — all layers visible, then <paramref name="layer"/> hidden — and asserts the
        /// drawings are all on <paramref name="layer"/> (at least one), and none survive with it hidden.</summary>
        public static void AssertOnItsOwnLayer(Action<IDebugDrawBuilder> emit, byte layer)
        {
            var shown = new DebugPrimitiveBuffer(65_536);
            emit(shown);
            var drawn = shown.GetFrame().ToArray().Where(IsDrawing).ToArray();
            Assert.True(drawn.Length > 0, "anti-vacuity: the fixture must make the gizmo draw something");
            var stray = drawn.Where(p => p.DebugLayer != layer).ToArray();
            Assert.True(stray.Length == 0,
                $"{stray.Length} of {drawn.Length} drawings are not on layer {layer}: "
                + string.Join(", ", stray.GroupBy(p => (p.Shape, p.DebugLayer)).Select(g => $"{g.Key.Shape}@{g.Key.DebugLayer}×{g.Count()}")));

            var hidden = new DebugPrimitiveBuffer(65_536);
            var mask = new LayerMask256();
            for (int i = 0; i < 256; i++) if (i != layer) mask.SetBit(i);
            hidden.SetActiveLayers(mask);
            emit(hidden);
            int left = hidden.GetFrame().ToArray().Count(IsDrawing);
            Assert.True(left == 0, $"with layer {layer} hidden the gizmo still drew {left} primitive(s)");
        }

        /// <summary>A primitive that is drawn (not a meta primitive, anchor or pick target).</summary>
        public static bool IsDrawing(DebugPrimitive p)
            => !DebugPrimitiveBuffer.IsProtected(p.Shape) && p.Shape != DebugPrimitiveShape.SpatialAnchor && !DebugPrimitiveBuffer.IsPickTarget(in p);
    }
}
