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
        /// <summary>
        /// ⭐ <c>CE-5048</c> (b) (user-approved 2026-11-23: "size the hides for tank — tank know how big it is"; Exposure Cycle §3 known-rot) — does the
        /// hull fit at <paramref name="at"/> in the pose it ARRIVES in: its length along <paramref name="heading"/> (the way it drives in), its box
        /// grown by <see cref="Margin"/> on every side for an arrival that is never exact. 📐 Measured on the Stechovice M1: hides sized for a SIDE-ON
        /// park (<see cref="Clear"/> with the cover's normal) were driven to nose-first, held at the wall, backed out and driven into again.
        /// </summary>
        public static bool FitsArriving(TerrainWorld world, Vector3 at, Vector2 heading, float length, float width, float height)
        {
            if (heading.LengthSquared() < 1e-6f) return Clear(world, at, Vector2.UnitX, length + 2f * Margin, width + 2f * Margin, height);
            var h = Vector2.Normalize(heading);
            return Clear(world, at, new Vector2(h.Y, -h.X), length + 2f * Margin, width + 2f * Margin, height);   // Clear's length runs across its normal ⇒ the normal ⟂ the heading
        }

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

        // ⭐ CE-5044 — through the world's spatial index (TerrainWorld.InsideFootprint — the same box, height band and polygon rule): this was a scan of
        //   every prism per hull sample, 333 k on Stechovice, the EQS solver's top cost there (handoff ui → terrain; the CE-4366 fix of the
        //   cover build, this caller missed). A deck above the hull (TopZ ≤ z or BaseZ ≥ z + height) is not in the way.
        private static bool Solid(TerrainWorld world, Vector2 p, float z, float height)
            => height > 0f ? world.InsideFootprint(p.X, p.Y, z, z + height) : world.InsideFootprint(p.X, p.Y);
    }
}
