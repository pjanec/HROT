using System;
using System.Numerics;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Canned test maps used across navigation unit tests.
    /// Each method returns a freshly-built <see cref="NavTestMap"/>.
    /// Maps can also be loaded from JSON fixtures via <see cref="NavTestMapLoader"/>.
    /// </summary>
    public static class NavTestMaps
    {
        // Helper: build a unit square polygon centred on (cx, cz) in the XZ plane.
        private static Vector3[] Square(float cx, float cz, float size = 10f)
        {
            float h = size * 0.5f;
            return new[]
            {
                new Vector3(cx - h, 0f, cz - h),
                new Vector3(cx + h, 0f, cz - h),
                new Vector3(cx + h, 0f, cz + h),
                new Vector3(cx - h, 0f, cz + h),
            };
        }

        /// <summary>
        /// Three polygons in a straight corridor: [0]-[1]-[2].
        /// Infantry layer.
        /// </summary>
        public static NavTestMap LoadCorridor()
            => new NavTestMapBuilder()
                .Layer(NavLayerMask.Infantry, b => b
                    .Polygon(0, Square(5f,  5f))
                    .Polygon(1, Square(15f, 5f))
                    .Polygon(2, Square(25f, 5f))
                    .Adjacent(0, 1)
                    .Adjacent(1, 2))
                .Build();

        /// <summary>
        /// Four polygons in an L-shaped path: [0]-[1]-[2]-[3].
        /// The turn is at polygon 2.
        /// Infantry layer.
        /// </summary>
        public static NavTestMap LoadLBend()
            => new NavTestMapBuilder()
                .Layer(NavLayerMask.Infantry, b => b
                    .Polygon(0, Square(5f,  5f))
                    .Polygon(1, Square(15f, 5f))
                    .Polygon(2, Square(25f, 5f))
                    .Polygon(3, Square(25f, 15f))
                    .Adjacent(0, 1)
                    .Adjacent(1, 2)
                    .Adjacent(2, 3))
                .Build();

        /// <summary>
        /// Two separate layers (Infantry + Vehicle) each with three polygons.
        /// </summary>
        public static NavTestMap LoadTwoLayers()
            => new NavTestMapBuilder()
                .Layer(NavLayerMask.Infantry, b => b
                    .Polygon(0, Square(5f,  5f))
                    .Polygon(1, Square(15f, 5f))
                    .Polygon(2, Square(25f, 5f))
                    .Adjacent(0, 1)
                    .Adjacent(1, 2))
                .Layer(NavLayerMask.Vehicle, b => b
                    .Polygon(10, Square(5f,  25f))
                    .Polygon(11, Square(15f, 25f))
                    .Polygon(12, Square(25f, 25f))
                    .Adjacent(0, 1)
                    .Adjacent(1, 2))
                .Build();

        /// <summary>
        /// Two polygons connected by an off-mesh jump link.
        /// Infantry layer.
        /// </summary>
        public static NavTestMap LoadOffMeshJump()
        {
            var link = new OffMeshLink
            {
                FromPolygonId = 0,
                ToPolygonId   = 1,
                StartPos      = new Vector3(10f, 0f, 5f),
                EndPos        = new Vector3(20f, 0f, 5f),
                Kind          = TraversalKind.Jump,
                Cost          = 5f,
            };
            return new NavTestMapBuilder()
                .Layer(NavLayerMask.Infantry, b => b
                    .Polygon(0, Square(5f,  5f))
                    .Polygon(1, Square(25f, 5f))
                    .OffMeshLink(link))
                .Build();
        }

        /// <summary>
        /// Four-polygon map: three in a corridor (0->1->2) plus one bypass polygon (3) connecting
        /// polygon 0 and polygon 2 north of the main route.  Polygon 1 is pre-blocked so initial
        /// path queries use the bypass (3).  Tests that need the main route call
        /// <c>NavmeshApi.UnblockPolygon(1)</c> first.
        /// Infantry layer.
        /// </summary>
        public static NavTestMap LoadReplan()
        {
            var map = new NavTestMapBuilder()
                .Layer(NavLayerMask.Infantry, b => b
                    .Polygon(0, Square(5f,  5f))
                    .Polygon(1, Square(15f, 5f))   // main route; blockable mid-test
                    .Polygon(2, Square(25f, 5f))
                    .Polygon(3, Square(15f, 15f))  // alternate bypass north of polygon 1
                    .Adjacent(0, 1)
                    .Adjacent(1, 2)
                    .Adjacent(0, 3)
                    .Adjacent(3, 2))
                .Build();
            // Pre-block the middle polygon so the initial path query returns results via polygon 3.
            // Tests that need the main path first call NavmeshApi.UnblockPolygon(1).
            map.Layers[0].Polygons[1].IsBlocked = true;
            return map;
        }

        /// <summary>
        /// Corridor with six polygons; tests crowd separation when multiple agents converge.
        /// Infantry layer.
        /// </summary>
        public static NavTestMap LoadCrowded()
            => new NavTestMapBuilder()
                .Layer(NavLayerMask.Infantry, b => b
                    .Polygon(0, Square(5f,  5f))
                    .Polygon(1, Square(15f, 5f))
                    .Polygon(2, Square(25f, 5f))
                    .Polygon(3, Square(35f, 5f))
                    .Polygon(4, Square(45f, 5f))
                    .Polygon(5, Square(55f, 5f))
                    .Adjacent(0, 1)
                    .Adjacent(1, 2)
                    .Adjacent(2, 3)
                    .Adjacent(3, 4)
                    .Adjacent(4, 5))
                .Build();

        /// <summary>
        /// Single polygon (dead-end); agent gets stuck if it overshoots its target.
        /// Infantry layer.
        /// </summary>
        public static NavTestMap LoadStuck()
            => new NavTestMapBuilder()
                .Layer(NavLayerMask.Infantry, b => b
                    .Polygon(0, Square(5f, 5f)))
                .Build();

        /// <summary>
        /// Very narrow path (single chain) that produces replanning frustration.
        /// Infantry layer.
        /// </summary>
        public static NavTestMap LoadFrustration()
            => new NavTestMapBuilder()
                .Layer(NavLayerMask.Infantry, b => b
                    .Polygon(0, Square(5f,  5f))
                    .Polygon(1, Square(15f, 5f))
                    .Polygon(2, Square(25f, 5f))
                    .Polygon(3, Square(35f, 5f))
                    .Adjacent(0, 1)
                    .Adjacent(1, 2)
                    .Adjacent(2, 3))
                .Build();

        /// <summary>
        /// Air layer with a no-fly zone between X=10 and X=20.
        /// </summary>
        public static NavTestMap LoadFlying()
            => new NavTestMapBuilder()
                .Layer(NavLayerMask.Air, b => b
                    .Polygon(0, Square(5f,  50f))
                    .Polygon(1, Square(15f, 50f))
                    .Polygon(2, Square(25f, 50f))
                    .Adjacent(0, 1)
                    .Adjacent(1, 2))
                .MinAltitude(0f)
                .MaxAltitude(200f)
                .NoFlyZone(new BoundingBox3D(
                    new Vector3(10f, 0f,   0f),
                    new Vector3(20f, 5f, 100f)))
                .Build();

        /// <summary>
        /// Naval layer with water surface type.
        /// </summary>
        public static NavTestMap LoadNaval()
            => new NavTestMapBuilder()
                .Layer(NavLayerMask.Naval, b => b
                    .Polygon(0, Square(5f,  5f), SurfaceType.Water)
                    .Polygon(1, Square(15f, 5f), SurfaceType.Water)
                    .Polygon(2, Square(25f, 5f), SurfaceType.Water)
                    .Adjacent(0, 1)
                    .Adjacent(1, 2))
                .Build();
    }
}
