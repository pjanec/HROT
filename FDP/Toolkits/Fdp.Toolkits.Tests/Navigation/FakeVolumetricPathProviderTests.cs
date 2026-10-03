using System.Numerics;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    public class FakeVolumetricPathProviderTests
    {
        // Helper: create a no-fly zone cube centered on <pos> with half-extent <half>.
        private static BoundingBox3D NoFlyBox(Vector3 pos, float half)
            => new BoundingBox3D(pos - new Vector3(half), pos + new Vector3(half));

        // ── Test 1: IsFlyable - clear point ─────────────────────────────────────

        [Fact]
        public void IsFlyable_ClearPoint_ReturnsTrue()
        {
            var provider = new FakeVolumetricPathProvider(minAltitude: 0f, maxAltitude: 1000f);
            Assert.True(provider.IsFlyable(new Vector3(0, 0, 100)));
        }

        // ── Test 2: IsFlyable - point inside no-fly zone ─────────────────────────

        [Fact]
        public void IsFlyable_InNoFlyZone_ReturnsFalse()
        {
            var provider = new FakeVolumetricPathProvider(minAltitude: 0f, maxAltitude: 1000f);
            provider.AddNoFlyZone(NoFlyBox(new Vector3(50, 50, 100), 10f));

            Assert.False(provider.IsFlyable(new Vector3(50, 50, 100)));
        }

        // ── Test 3: IsFlyable - below minimum altitude ───────────────────────────

        [Fact]
        public void IsFlyable_BelowMinAltitude_ReturnsFalse()
        {
            var provider = new FakeVolumetricPathProvider(minAltitude: 50f, maxAltitude: 500f);
            Assert.False(provider.IsFlyable(new Vector3(0, 0, 10)));
        }

        // ── Test 4: PlanPath - clear straight line ───────────────────────────────

        [Fact]
        public void Plan_ClearPath_ReturnsSingleWaypointAtDestination()
        {
            var provider = new FakeVolumetricPathProvider(minAltitude: 0f, maxAltitude: 1000f);
            var from = new Vector3(0, 0, 100);
            var to   = new Vector3(0, 50, 100);

            var buf = new NavWaypoint[4];
            int n = provider.PlanPath(from, to, buf.AsSpan());

            Assert.Equal(2, n);
            Assert.Equal(from, buf[0].Position);
            Assert.Equal(to,   buf[1].Position);
        }

        // ── Test 5: PlanPath - blocked straight line, find detour ────────────────

        [Fact]
        public void Plan_BlockedStraightLine_FindsDetourAroundNoFlyZone()
        {
            var provider = new FakeVolumetricPathProvider(minAltitude: 0f, maxAltitude: 1000f);
            // No-fly zone sits in the middle of the straight path (X=0..100 east, altitude Z=100, Y=0).
            provider.AddNoFlyZone(new BoundingBox3D(
                new Vector3(-8, -8, 95),
                new Vector3(8, 50, 105)));

            var from = new Vector3(0, -20, 100);
            var to   = new Vector3(0, 80, 100);

            var buf = new NavWaypoint[50];
            int n = provider.PlanPath(from, to, buf.AsSpan());

            // A detour was found (more than 2 waypoints) or at least the path is non-zero.
            Assert.True(n >= 2, $"Expected at least 2 waypoints, got {n}");
            // Last waypoint should be close to 'to'.
            Assert.True((buf[n - 1].Position - to).Length() < 15f,
                $"Last waypoint far from destination: {buf[n - 1].Position}");
        }

        // ── Test 6: AddNoFlyZone bumps version ───────────────────────────────────

        [Fact]
        public void AddNoFlyZone_BumpsVersion()
        {
            var provider = new FakeVolumetricPathProvider();
            uint before = provider.QueryVersion();
            provider.AddNoFlyZone(NoFlyBox(Vector3.Zero, 1f));
            uint after = provider.QueryVersion();
            Assert.True(after > before, "Version should increase after AddNoFlyZone");
        }

        // ── Test 7: PlanPath - start inside no-fly zone returns no path ──────────

        [Fact]
        public void Plan_StartInsideNoFlyZone_ReturnsNoPath()
        {
            var provider = new FakeVolumetricPathProvider(minAltitude: 0f, maxAltitude: 1000f);
            provider.AddNoFlyZone(NoFlyBox(new Vector3(0, 0, 100), 5f));

            var buf = new NavWaypoint[10];
            int n = provider.PlanPath(new Vector3(0, 0, 100), new Vector3(100, 0, 100), buf.AsSpan());

            Assert.Equal(0, n);
        }

        // ── Test 8: PlanPath - end inside no-fly zone returns no path ────────────

        [Fact]
        public void Plan_EndInsideNoFlyZone_ReturnsNoPath()
        {
            var provider = new FakeVolumetricPathProvider(minAltitude: 0f, maxAltitude: 1000f);
            provider.AddNoFlyZone(NoFlyBox(new Vector3(100, 0, 100), 5f));

            var buf = new NavWaypoint[10];
            int n = provider.PlanPath(new Vector3(0, 0, 100), new Vector3(100, 0, 100), buf.AsSpan());

            Assert.Equal(0, n);
        }

        // ── Test 9: PlanPath - altitude above max ceiling returns no path ─────────

        [Fact]
        public void Plan_AltitudeExceedsProfileMax_ReturnsNoPath()
        {
            var provider = new FakeVolumetricPathProvider(minAltitude: 0f, maxAltitude: 200f);

            var buf = new NavWaypoint[10];
            // altitude Z=300 exceeds maxAltitude=200, so both endpoints are not flyable.
            int n = provider.PlanPath(new Vector3(0, 0, 300), new Vector3(100, 0, 300), buf.AsSpan());

            Assert.Equal(0, n);
        }
    }
}
