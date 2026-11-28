using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace GizmoMap.Contracts.Tests
{
    /// <summary>
    /// ⭐⭐ Simulated devices SD-S5v (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13e D-122 / D-123) — THE PanelButton hit zone: a meta primitive
    /// (classified, protected, never drawn) carrying the element and the chip's canvas rectangle; the hit test runs in CANVAS coordinates, so the same chip is
    /// hit wherever the canvas lands in the view — at the canvas's own size, and letterboxed into a wider or a taller view (<see cref="ViewCanvas.Fit"/>); a
    /// point in a letterbox band hits nothing; the topmost of two overlapping buttons wins; 0 B. 🔴 Red-proof (compiles): <c>ToCanvas</c> ignoring the fit's
    /// origin ⇒ the letterboxed hit misses.
    /// </summary>
    public class PanelButtonsTests
    {
        private static readonly DebugPrimitive[] Frame =
        {
            DebugPrimitive.MakeLine(new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 0f), new Rgba32(255, 0, 0, 255)),
            PanelButtons.Make(0, 0, new Vector2(1000f, 300f), new Vector2(200f, 50f), PanelButtonState.Normal, 82, 253),      // RANGE
            PanelButtons.Make(0, 3, new Vector2(1000f, 400f), new Vector2(200f, 50f), PanelButtonState.Disabled, 0, 253),     // SELECT
            PanelButtons.Make(1, 0, new Vector2(1150f, 420f), new Vector2(100f, 50f), PanelButtonState.Lit, 0, 253),          // a switch over SELECT's end
        };

        [Fact]
        public void SDS5v_APanelButton_IsAProtectedMetaPrimitive_NeverDrawn()
        {
            Assert.Equal(PrimitiveClass.Meta, PrimitiveKind.ClassOfShape(DebugPrimitiveShape.PanelButton));
            Assert.True(PrimitiveKind.IsMeta(DebugPrimitiveShape.PanelButton));
            Assert.True(PrimitiveKind.IsProtected(in Frame[1]));
            Assert.Equal((0, 3, PanelButtonState.Disabled), (Frame[2].PanelKind, Frame[2].PanelElement, Frame[2].PanelState));
            Assert.Equal((1000f, 400f, 200f, 50f), (Frame[2].PanelX, Frame[2].PanelY, Frame[2].PanelW, Frame[2].PanelH));
            Assert.Equal(CoordinateSpace.ViewCanvas, Frame[1].Space);
        }

        [Theory]
        [InlineData(1280f, 720f)]     // the canvas itself
        [InlineData(1920f, 800f)]     // wider: bands left and right
        [InlineData(1000f, 1000f)]    // taller: bands above and below
        public void SDS5v_TheHitTest_IsInCanvasCoordinates_AtAnyViewSize(float w, float h)
        {
            var view = new Vector2(w, h);
            Assert.Equal(1, PanelButtons.Hit(Frame, PanelButtons.ToCanvas(PanelButtons.CentreOnView(in Frame[1], view), view)));
            Assert.Equal(2, PanelButtons.Hit(Frame, PanelButtons.ToCanvas(PanelButtons.CentreOnView(in Frame[2], view), view)));
            ViewCanvas.Fit(view, out var origin, out float scale);
            var corner = origin + new Vector2(1000f, 300f) * scale;
            Assert.Equal(1, PanelButtons.Hit(Frame, PanelButtons.ToCanvas(corner + new Vector2(0.5f, 0.5f), view)));    // just inside the top-left corner
            Assert.Equal(-1, PanelButtons.Hit(Frame, PanelButtons.ToCanvas(corner - new Vector2(1f, 1f), view)));       // just outside
            if (origin.X > 1f) Assert.Equal(-1, PanelButtons.Hit(Frame, PanelButtons.ToCanvas(new Vector2(origin.X * 0.5f, view.Y * 0.5f), view)));   // a band
            Assert.Equal(-1, PanelButtons.Hit(Frame, PanelButtons.ToCanvas(view * 0.5f, view)));                         // the view's centre: no button
        }

        [Fact]
        public void SDS5v_TheTopmostButton_Wins_AndFindNamesTheElement_AndNothingAllocates()
        {
            var overlap = new Vector2(1175f, 430f);   // on SELECT and on the switch drawn after it
            Assert.Equal(3, PanelButtons.Hit(Frame, overlap));
            Assert.Equal(2, PanelButtons.Find(Frame, 0, 3));
            Assert.Equal(-1, PanelButtons.Find(Frame, 2, 0));
            for (int i = 0; i < 10; i++) PanelButtons.Hit(Frame, overlap);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) { PanelButtons.Hit(Frame, overlap); PanelButtons.ToCanvas(overlap, new Vector2(1920f, 800f)); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
