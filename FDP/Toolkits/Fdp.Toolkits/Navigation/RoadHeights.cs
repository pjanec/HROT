using System;
using System.Numerics;
using CarKinem.Road;
using Fdp.Toolkit.World;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// ⭐ CE-1034 H5c (TH-L, docs/DESIGN_Terrain_Height.md §3.4) — the height of a point on a road. Roads carry a ground-relative
    /// height per node (<see cref="RoadNode.AboveGround"/>); a point's height is resolved in two steps:
    /// <list type="number">
    ///   <item>a NOMINAL height — the higher of (a) the two nodes' absolute heights (ground + above-ground) interpolated along the
    ///   segment, and (b) the ground at the point + the interpolated above-ground. (a) carries a bridge level across a valley whose
    ///   ground drops away under it; (b) keeps a road on the ground over a hump between two nodes;</item>
    ///   <item>SNAPPED to the real surface: the highest surface at or within a step above the nominal (<see cref="IWorldQuery.SurfaceZ"/>)
    ///   — the deck, the ramp, or the ground under a bridge.</item>
    /// </list>
    /// Every place a vehicle JOINS a road takes this height (the route planner's access and exit points and its road legs), so a unit
    /// under a bridge sent along the road over it does not drive the deck's line at the valley's height.
    /// </summary>
    public static class RoadHeights
    {
        /// <summary>The nominal height (step 1) at <paramref name="along"/> metres along <paramref name="segment"/>.</summary>
        public static float Nominal(in RoadNetworkBlob roads, int segment, float along, Vector2 at, Func<float, float, float> ground)
        {
            var seg = roads.Segments[segment];
            var n0 = roads.Nodes[seg.StartNodeIndex];
            var n1 = roads.Nodes[seg.EndNodeIndex];
            float s = seg.Length > 0f ? Math.Clamp(along / seg.Length, 0f, 1f) : 0f;
            float z0 = ground(n0.Position.X, n0.Position.Y) + n0.AboveGround;
            float z1 = ground(n1.Position.X, n1.Position.Y) + n1.AboveGround;
            float carried = z0 + (z1 - z0) * s;
            float draped = ground(at.X, at.Y) + n0.AboveGround + (n1.AboveGround - n0.AboveGround) * s;
            return MathF.Max(carried, draped);
        }

        /// <summary>The road's height at <paramref name="at"/> (step 2: snapped); with no world, the nominal on a flat ground at 0.</summary>
        public static float At(in RoadNetworkBlob roads, int segment, float along, Vector2 at, IWorldQuery? world)
        {
            if (world == null) return Nominal(roads, segment, along, at, static (_, _) => 0f);
            float nominal = Nominal(roads, segment, along, at, world.GroundHeightAt);
            return world.SurfaceZ(at.X, at.Y, nominal);
        }
    }
}
