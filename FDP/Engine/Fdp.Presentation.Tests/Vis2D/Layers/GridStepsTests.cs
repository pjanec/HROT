using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Layers;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Layers
{
    /// <summary>
    /// ⭐ <c>CE-1288</c> (docs/DESIGN_Map_3D_Mode.md §6ac) — the 2-D helper grid: thin minor lines, thicker major lines,
    /// the step chosen by the zoom. 🔒 User, 2026-10-23: <i>"the 2d map is missing any aux grid system with thicker gray lines for larger
    /// step grid and thinner gray lines for fines step grid, with auto selection of the grid stepping based on zoom"</i>.
    /// Pure rails (<see cref="GridSteps"/>, <see cref="GridMapLayer.ViewBounds"/>) — drawing itself needs a window and is not seen here.
    /// </summary>
    public class GridStepsTests
    {
        [Fact]
        public void TheLadderIs_1_2_5_TimesPowersOfTen()
        {
            Assert.Equal(new[] { 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000 },
                         Enumerable.Range(-3, 13).Select(GridSteps.Rung).Select(v => Math.Round(v, 6)).ToArray());
        }

        [Theory]
        //   zoom (px/m)  minor (m)  major (m)  every
        [InlineData(1f,     20d,      100d,      5)]    // 18 px asks for 18 m → the 20 m rung
        [InlineData(0.1f,   200d,     1000d,     5)]    // zoomed far out: 200 m / 1 km
        [InlineData(100f,   0.2d,     1d,        5)]    // zoomed far in: 20 cm / 1 m
        [InlineData(10f,    2d,       10d,       5)]    // 18 px = 1.8 m → 2 m
        [InlineData(3.6f,   5d,       20d,       4)]    // 18 px = 5 m exactly → the 5 m rung stays (not skipped to 10), major two rungs up
        [InlineData(2f,     10d,      50d,       5)]
        public void TheStepFollowsTheZoom(float zoom, double minor, double major, int every)
        {
            var s = GridSteps.For(zoom);
            Assert.True(s.IsValid);
            Assert.Equal(minor, s.Minor, 6);
            Assert.Equal(major, s.Major, 6);
            Assert.Equal(every, s.MajorEvery);
        }

        /// <summary>For any zoom the minor lines are never closer than the floor and never wastefully far (the ladder's widest gap is ×2.5),
        /// and every major line lies on a minor one.</summary>
        [Fact]
        public void TheLinesStayAReadableDistanceApart_AtEveryZoom()
        {
            for (double z = 0.05; z <= 200; z *= 1.07)
            {
                var s = GridSteps.For((float)z);
                double px = s.Minor * (float)z;
                Assert.True(px >= GridSteps.MinMinorPixels * 0.999, $"zoom {z}: minor lines {px:F2} px apart");
                Assert.True(px < GridSteps.MinMinorPixels * 2.5 + 0.01, $"zoom {z}: minor lines {px:F2} px apart (too sparse)");
                Assert.Equal(Math.Round(s.Major / s.Minor), s.Major / s.Minor, 6);   // a whole number of minor steps
                Assert.True(s.MajorEvery >= 4 && s.MajorEvery <= 5);
                Assert.Equal(s.Major, s.Minor * s.MajorEvery, 6);
            }
        }

        [Fact]
        public void TheLinesLieOnMultiplesOfTheStep_AndEveryNthIsMajor()
        {
            var s = GridSteps.For(2f);   // minor 10 m, major 50 m
            var lines = GridSteps.Lines(-120, 120, s).ToList();

            Assert.Equal(25, lines.Count);                                              // -120 … 120 every 10
            Assert.Equal(-120d, lines[0].Position);
            Assert.Equal(120d, lines[^1].Position);
            Assert.All(lines, l => Assert.Equal(0d, l.Position % 10d));
            Assert.Equal(new[] { -100d, -50d, 0d, 50d, 100d }, lines.Where(l => l.Major).Select(l => l.Position).ToArray());
            Assert.Equal(lines.Select(l => l.Position).OrderBy(p => p), lines.Select(l => l.Position));   // ascending
        }

        [Fact]
        public void ALineExactlyOnTheViewEdgeIsKept_AndAnEmptyViewHasNone()
        {
            var s = GridSteps.For(2f);
            Assert.Equal(new[] { 10d, 20d }, GridSteps.Lines(10, 20, s).Select(l => l.Position).ToArray());
            Assert.Empty(GridSteps.Lines(11, 19, s));
            Assert.Empty(GridSteps.Lines(20, 10, s));                                   // inverted
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void ADegenerateZoom_DrawsNothing(float zoom)
        {
            var s = GridSteps.For(zoom);
            Assert.False(s.IsValid);
            Assert.Empty(GridSteps.Lines(-100, 100, s));
        }

        [Fact]
        public void ANonFiniteOrAbsurdlyLargeView_DrawsNothing_RatherThanStalling()
        {
            var s = GridSteps.For(1f);
            Assert.Empty(GridSteps.Lines(double.NaN, 100, s));
            Assert.Empty(GridSteps.Lines(-1e12, 1e12, s));                              // would be 10^11 lines
        }

        /// <summary>
        /// The view is the CAMERA's, not the cursor's — the first version of the layer took its bounds around the mouse. The 2-D map is
        /// north-up, so screen-down is world-south; the rectangle is the same whichever way the corners come back.
        /// </summary>
        [Fact]
        public void TheViewIsTheCamerasRectangle_NotTheMouses()
        {
            var camera = new MapCamera();
            camera.Zoom = 2f;
            camera.Target = new Vector2(100, 200);
            camera.Offset = new Vector2(400, 300);     // the screen's centre is the target

            var (min, max) = GridMapLayer.ViewBounds(camera, 800, 600);

            Assert.Equal(new Vector2(-100, 50), min);   // 400 px / 2 = 200 m either side in X, 300 px / 2 = 150 m in Y
            Assert.Equal(new Vector2(300, 350), max);
        }
    }
}
