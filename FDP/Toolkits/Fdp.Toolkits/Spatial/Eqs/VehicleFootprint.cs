using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ <c>CE-2165</c> V3 (docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md §3) — a VEHICLE's hide point is sized by its hull:
    /// a baked cover point (<see cref="TerrainCoverProvider.StandOff"/> from the wall, made for a man) is pushed out to half the hull's
    /// width + <see cref="Margin"/>, and kept only where the hull's box — length ALONG the wall, width toward it, its height — is clear of
    /// every solid that overlaps that height. A person's cover points are untouched (<see cref="CoverPointsGenerator.VehicleFootprint"/>).
    /// </summary>
    public static class VehicleFootprint
    {
        /// <summary>The gap between the hull's side and the wall (m).</summary>
        public const float Margin = 0.5f;

        /// <summary>The longest step between box samples (m): a 0.5 m wall cannot slip between two of them.</summary>
        public const float SampleStep = 0.4f;

        /// <summary>The hull of <paramref name="self"/>: <c>VehicleParams</c> length × width (else the square inside its collider) × its
        /// collider height. False when it is not a vehicle or its size is unknown.</summary>
        public static bool Hull(ISimulationView view, Entity self, out float length, out float width, out float height)
        {
            length = width = height = 0f;
            if (self.IsNull || !VehicleCover.IsVehicle(view, self)) return false;
            var repo = view as EntityRepository;
            if ((repo == null || repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>())
                && view.HasComponent<global::CarKinem.Core.VehicleParams>(self))
            {
                ref readonly var vp = ref view.GetComponentRO<global::CarKinem.Core.VehicleParams>(self);
                length = vp.Length; width = vp.Width;
            }
            bool collider = (repo == null || repo.IsComponentTypeRegistered<PhysicsCollider>()) && view.HasComponent<PhysicsCollider>(self);
            if (collider)
            {
                ref readonly var c = ref view.GetComponentRO<PhysicsCollider>(self);
                height = c.Height;
                if (length <= 0f || width <= 0f) length = width = c.Radius * MathF.Sqrt(2f);
            }
            return length > 0f && width > 0f;
        }

        /// <summary>How far from the wall the hull's centre stands: half its width + <see cref="Margin"/>.</summary>
        public static float StandOff(float width) => width * 0.5f + Margin;

        /// <summary>
        /// True when the hull's box centred at <paramref name="at"/> — <paramref name="length"/> across <paramref name="towardCover"/>,
        /// <paramref name="width"/> along it — touches no solid of <paramref name="world"/> that overlaps [at.Z, at.Z + height] (height ≤ 0 ⇒
        /// any). Sampled every <see cref="SampleStep"/> over the box.
        /// </summary>
        public static bool Clear(TerrainWorld world, Vector3 at, Vector2 towardCover, float length, float width, float height)
        {
            if (towardCover.LengthSquared() < 1e-6f) towardCover = Vector2.UnitX;
            var f = Vector2.Normalize(towardCover);
            var t = new Vector2(-f.Y, f.X);
            int nl = Math.Max(1, (int)MathF.Ceiling(length / SampleStep)), nw = Math.Max(1, (int)MathF.Ceiling(width / SampleStep));
            var c = new Vector2(at.X, at.Y);
            for (int i = 0; i <= nl; i++)
                for (int j = 0; j <= nw; j++)
                {
                    var p = c + t * (length * (i / (float)nl - 0.5f)) + f * (width * (j / (float)nw - 0.5f));
                    if (Solid(world, p, at.Z, height)) return false;
                }
            return true;
        }

        private static bool Solid(TerrainWorld world, Vector2 p, float z, float height)
        {
            foreach (var prism in world.Prisms)
            {
                if (p.X < prism.Min.X || p.Y < prism.Min.Y || p.X > prism.Max.X || p.Y > prism.Max.Y) continue;
                if (height > 0f && (prism.TopZ <= z || prism.BaseZ >= z + height)) continue;   // a deck above the hull is not in the way
                if (PolygonMath.Contains(prism.Footprint, p)) return true;
            }
            return false;
        }
    }
}
