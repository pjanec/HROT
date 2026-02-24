using System.Numerics;
using FDP.Toolkit.Physics.Math;
using Xunit;

namespace FDP.Toolkit.Physics.Tests
{
    /// <summary>
    /// Unit tests for <see cref="Intersection2D.RaycastCircle"/> (BCS-P4-T2).
    /// All tests are pure (no ECS world required).
    /// </summary>
    public class Intersection2DTests
    {
        // ── Test 1 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A ray along the X-axis that passes directly through the circle's centre
        /// must return a hit with t near the entry edge (t ≈ 0.4 on a 10-unit ray,
        /// entering at x = −1).
        /// </summary>
        [Fact]
        public void RaycastCircle_HitsCenter()
        {
            // Arrange: 10-unit ray from (−5,0) to (5,0). Circle centred at origin, radius 1.
            // Entry at x=−1 → t = (−5 − (−1)) / (5 − (−5)) = 4/10 = 0.4
            var start  = new Vector2(-5f, 0f);
            var end    = new Vector2( 5f, 0f);
            var center = new Vector2( 0f, 0f);

            bool hit = Intersection2D.RaycastCircle(start, end, center, 1f, out float t);

            Assert.True(hit);
            Assert.InRange(t, 0.35f, 0.45f);
        }

        // ── Test 2 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A ray that passes 4 m above the circle centre must return no hit.
        /// </summary>
        [Fact]
        public void RaycastCircle_MissesCircle_WhenRayPassesBeside()
        {
            // Arrange: ray at y=5, 4 m above a radius-1 circle at the origin.
            var start  = new Vector2(-5f, 5f);
            var end    = new Vector2( 5f, 5f);
            var center = new Vector2( 0f, 0f);

            bool hit = Intersection2D.RaycastCircle(start, end, center, 1f, out _);

            Assert.False(hit);
        }

        // ── Test 3 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A ray that stops 2 m before the circle's near edge must return no hit.
        /// </summary>
        [Fact]
        public void RaycastCircle_MissesCircle_WhenSegmentTooShort()
        {
            // Arrange: ray from (−5,0) to (−2,0). Circle at (3,0) with radius 1.
            // Near edge of circle at x=2. Ray ends at x=−2 so it never reaches the circle.
            var start  = new Vector2(-5f, 0f);
            var end    = new Vector2(-2f, 0f);
            var center = new Vector2( 3f, 0f);

            bool hit = Intersection2D.RaycastCircle(start, end, center, 1f, out _);

            Assert.False(hit);
        }

        // ── Test 4 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// A ray that crosses a full circle diameter must return the entry t (not the exit t).
        /// Entry at x=−1 → t≈0.4; exit at x=+1 → t≈0.6.
        /// </summary>
        [Fact]
        public void RaycastCircle_ReturnsTMin_WhenTwoIntersections()
        {
            var start  = new Vector2(-5f, 0f);
            var end    = new Vector2( 5f, 0f);
            var center = new Vector2( 0f, 0f);

            bool hit = Intersection2D.RaycastCircle(start, end, center, 1f, out float t);

            Assert.True(hit);
            // Entry t ≈ 0.4, not the exit at ≈ 0.6.
            Assert.InRange(t, 0.35f, 0.45f);
        }

        // ── Test 5 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// When the ray starts inside the circle, t1 &lt; 0 (entry behind start), so the
        /// implementation must return t2 (the exit intersection) which lies in [0,1].
        /// Exit at x=+1 on a 5-unit ray → t ≈ 0.2.
        /// </summary>
        [Fact]
        public void RaycastCircle_HitsCircle_WhenRayStartsInside()
        {
            // Arrange: ray from (0,0) — inside a radius-1 circle at origin — to (5,0).
            // t2 = (+1 − 0) / 5 = 0.2
            var start  = new Vector2(0f, 0f);
            var end    = new Vector2(5f, 0f);
            var center = new Vector2(0f, 0f);

            bool hit = Intersection2D.RaycastCircle(start, end, center, 1f, out float t);

            Assert.True(hit);
            // Exit t ≈ 0.2 (at x=1 on 5-unit ray).
            Assert.InRange(t, 0.15f, 0.25f);
        }
    }
}
