using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ Sight and placement over the resident terrain for EQS — the SAME occluder perception uses
    /// (<see cref="TerrainWorld.SegmentBlocked"/>, R-174: one source), with perception's eye heights
    /// (docs/designs/eqs-2/EQS_Design_v1.3_final.md §19.5).
    /// <para>⭐ Terrain, plus every live VEHICLE's box (<c>CE-3142</c>, P-7a O5 — read from the query's own view, <see cref="VehicleCover"/>);
    /// units are not cover. ⛔ SUPERSEDED `2026-10-09`: "Terrain only: vehicles and units are not cover (they move)" (EQS §19.5) — a
    /// standing car is, and a moving one hides what is behind it at that instant. ⭐ No terrain resident ⇒ sight is UNKNOWN —
    /// callers do nothing rather than guess (the retired <c>BlockedLosService</c> answered "always blocked").</para>
    /// </summary>
    public static class EqsTerrainSight
    {
        /// <summary>The resident terrain on <paramref name="view"/>, or null. ⭐ Synced into the solver snapshot
        /// (<c>EntityRepository.Sync</c>).</summary>
        public static TerrainWorld? World(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonManaged<TerrainWorld>()
                ? repo.GetSingletonManaged<TerrainWorld>()
                : null;

        /// <summary>The eye heights of <paramref name="e"/> (its <see cref="SensorMount"/>, else a soldier's).</summary>
        public static SensorMount Mount(ISimulationView view, Entity e)
            => !e.IsNull && view.IsAlive(e) && view.HasComponent<SensorMount>(e)
                ? view.GetComponentRO<SensorMount>(e)
                : TerrainWorldLosStrategy.DefaultMount;

        /// <summary>The sight a test uses: the one it was given, else the resident terrain's; null when neither exists
        /// (sight unknown ⇒ the test does nothing).</summary>
        public static ILosService? Sight(ISimulationView view, ILosService? injected)
        {
            if (injected != null) return injected;
            var world = World(view);
            // ⭐ R-219 — the EQS solver's view is its snapshot: the doors it sees are the doors of that snapshot
            return world == null ? null : new TerrainLosService(world, world.Doors.Count > 0 ? DoorStates.Of(view, world) : null,
                VehicleCover.Collect(view, world.Materials, standingOnly: false));   // ⭐ CE-3142 — the view's vehicles, once per batch
        }

        /// <summary>True when <paramref name="p"/> (XY) is inside a building or wall footprint.</summary>
        public static bool InsideSolid(TerrainWorld world, Vector2 p)
        {
            foreach (var prism in world.Prisms)
            {
                if (p.X < prism.Min.X || p.Y < prism.Min.Y || p.X > prism.Max.X || p.Y > prism.Max.Y) continue;
                if (PolygonMath.Contains(prism.Footprint, p)) return true;
            }
            return false;
        }

        /// <summary>
        /// Puts a sampled point on the ground: Z from <see cref="TerrainWorld.SurfaceZ"/> (the surface reachable from
        /// <paramref name="zHint"/>), rejected inside a solid. Without a terrain the point keeps <paramref name="zHint"/>.
        /// </summary>
        public static bool TryPlace(TerrainWorld? world, Vector2 p, float zHint, out Vector3 placed)
        {
            if (world == null) { placed = new Vector3(p, zHint); return true; }
            if (InsideSolid(world, p)) { placed = default; return false; }
            placed = new Vector3(p, world.SurfaceZ(p.X, p.Y, zHint));
            return true;
        }
    }
}
