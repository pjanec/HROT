using System.Numerics;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ The 3-D sight seam EQS tests ask (<c>CE-210</c> (a)/(c), docs/designs/eqs-2/EQS_Design_v1.3_final.md §19.5).
    /// Endpoints are engine-space points (Z up) with the eye/aim heights ALREADY applied by the caller from the entity's
    /// <c>SensorMount</c> — ⛔ never a service-wide constant.
    /// <para>Implementations: <see cref="TerrainLosService"/> (the default — the resident terrain world, the occluder perception
    /// uses) and <c>Hrot.Stride.Core.StrideRaycastLosService</c> (Bullet raycasts). A test injects its own.</para>
    /// ⛔ SUPERSEDED: <c>HasCheapLineOfSight(Vector2, Vector2)</c> (2-D, CE-210 ③) and its only implementation
    /// <c>BlockedLosService</c> ("always blocked" — the reason the cover query never rejected anything).
    /// </summary>
    public interface ILosService
    {
        /// <summary>True when nothing blocks the segment <paramref name="eye"/>→<paramref name="aim"/>.</summary>
        bool HasLineOfSight(Vector3 eye, Vector3 aim);
    }

    /// <summary>⭐ Sight over the resident <see cref="TerrainWorld"/> — <see cref="TerrainWorld.SegmentBlocked"/>, the same
    /// test perception's <c>TerrainWorldLosStrategy</c> makes (R-174: one source). Terrain only: units are not cover.</summary>
    public sealed class TerrainLosService : ILosService
    {
        private readonly Fdp.Toolkit.World.IWorldQuery _world;   // ⭐ CE-1035 Q1 — the EQS sight POLICY over the world query (WQ-B)

        private readonly System.Collections.Generic.IReadOnlyList<TerrainPrism>? _vehicles;

        /// <param name="doors">⭐ R-219 — the door states of the view the query runs on; null = as authored.</param>
        /// <param name="vehicles">⭐ <c>CE-3142</c> (P-7a O5) — the live vehicle boxes of that view (<see cref="VehicleCover.Collect"/>); null = none.</param>
        public TerrainLosService(Fdp.Toolkit.World.IWorldQuery world, System.Collections.Generic.IReadOnlyList<TerrainPrism>? vehicles = null)
        {
            _world = world ?? throw new System.ArgumentNullException(nameof(world));
            _vehicles = vehicles;
        }

        /// <summary>Convenience for a bare terrain (tests, tools): the same policy over <see cref="TerrainWorldQuery"/>.</summary>
        public TerrainLosService(TerrainWorld world, DoorStates? doors = null, System.Collections.Generic.IReadOnlyList<TerrainPrism>? vehicles = null)
            : this(new TerrainWorldQuery(world ?? throw new System.ArgumentNullException(nameof(world)), doors), vehicles) { }

        /// <inheritdoc/>
        public bool HasLineOfSight(Vector3 eye, Vector3 aim)
            => !_world.SightBlocked(eye, aim) && (_vehicles == null || !VehicleCover.Blocks(_vehicles, eye, aim));
    }
}
