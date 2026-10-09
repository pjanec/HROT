using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐⭐ <c>CE-3142</c> (P-7a O5, R-243) — a vehicle is cover READ LIVE, never baked: the AI's sight (<see cref="EqsTerrainSight.Sight"/>)
    /// treats every vehicle's box as opaque, and the cover generator adds points round every STANDING vehicle — both from the query's
    /// own view, so a car that drives away stops being cover on the next query (📄 docs/DESIGN_Peek_And_Fire.md §9.2 O5, §9.7).
    /// <para>A vehicle = a <c>VehicleParams</c> that is not a pedestrian, else a bare <c>VehicleState</c> (<c>PathRequests.IsVehicle</c>'s
    /// rule), with a collider of known height, and not a static obstacle (that one is terrain already, R-243). Its box: length × width
    /// from <c>VehicleParams</c> (else a square inside its collider circle) × the collider height, along its heading, of
    /// <c>car-body</c>. Bullets still hit it as a vehicle (its armour, the narrow phase) and the navmesh ignores it (O5 ③ ④).</para>
    /// </summary>
    public static class VehicleCover
    {
        /// <summary>Below this speed (m/s) a vehicle is STANDING and its sides are cover points (O5 ②, as built: the speed now).</summary>
        public const float StandingSpeed = 0.3f;

        /// <summary>The material a vehicle's box is made of (sight 0 — a car body hides what is behind it).</summary>
        public const string Material = "car-body";

        /// <summary>True when <paramref name="e"/> is a vehicle (see the class remarks).</summary>
        public static bool IsVehicle(ISimulationView view, Entity e)
        {
            var repo = view as EntityRepository;
            if ((repo == null || repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>())
                && view.HasComponent<global::CarKinem.Core.VehicleParams>(e))
                return view.GetComponentRO<global::CarKinem.Core.VehicleParams>(e).Class != global::CarKinem.Core.VehicleClass.Pedestrian;
            return (repo == null || repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleState>())
                   && view.HasComponent<global::CarKinem.Core.VehicleState>(e);
        }

        /// <summary>True when the vehicle moves slower than <see cref="StandingSpeed"/>: its <c>VehicleState.Speed</c>, else its <see cref="SimVelocity"/>.</summary>
        public static bool IsStanding(ISimulationView view, Entity e)
        {
            var repo = view as EntityRepository;
            if ((repo == null || repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleState>())
                && view.HasComponent<global::CarKinem.Core.VehicleState>(e))
                return MathF.Abs(view.GetComponentRO<global::CarKinem.Core.VehicleState>(e).Speed) < StandingSpeed;
            if ((repo == null || repo.IsComponentTypeRegistered<SimVelocity>()) && view.HasComponent<SimVelocity>(e))
                return view.GetComponentRO<SimVelocity>(e).Linear.Length() < StandingSpeed;
            return true;
        }

        /// <summary>
        /// Every live vehicle's box on <paramref name="view"/> (<paramref name="standingOnly"/>: only the standing ones), or null when there
        /// is none — so a world with no vehicles allocates nothing (R-220). Built once per test batch, never per candidate.
        /// </summary>
        public static List<TerrainPrism>? Collect(ISimulationView view, TerrainMaterialLibrary? library, bool standingOnly)
        {
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<PhysicsCollider>()) return null;
            List<TerrainPrism>? boxes = null;
            TerrainMaterial? material = null;
            foreach (var e in view.Query().With<SimTransform>().WithComponentId(GlobalComponentIds.PhysicsCollider).Build())
            {
                if (!view.IsAlive(e) || TerrainObstacles.IsObstacle(view, e) || !IsVehicle(view, e)) continue;
                if (standingOnly && !IsStanding(view, e)) continue;
                ref readonly var collider = ref view.GetComponentRO<PhysicsCollider>(e);
                if (collider.Height <= 0f) continue;   // unknown height: says nothing about what it hides
                material ??= TerrainObstacles.MaterialOf(library ?? TerrainMaterialLibrary.Shared, Material, out _);
                (boxes ??= new List<TerrainPrism>()).Add(BoxOf(view, e, collider, material));
            }
            return boxes;
        }

        private static TerrainPrism BoxOf(ISimulationView view, Entity e, in PhysicsCollider collider, TerrainMaterial material)
        {
            ref readonly var t = ref view.GetComponentRO<SimTransform>(e);
            float length = 0f, width = 0f;
            var repo = view as EntityRepository;
            if ((repo == null || repo.IsComponentTypeRegistered<global::CarKinem.Core.VehicleParams>())
                && view.HasComponent<global::CarKinem.Core.VehicleParams>(e))
            {
                ref readonly var vp = ref view.GetComponentRO<global::CarKinem.Core.VehicleParams>(e);
                length = vp.Length; width = vp.Width;
            }
            if (length <= 0f || width <= 0f) length = width = collider.Radius * MathF.Sqrt(2f);   // the square inside its circle
            return TerrainObstacles.PrismOf(t.Position, TerrainObstacles.Yaw(t.Rotation),
                new ObstacleShape { Length = length, Width = width, Height = collider.Height }, material, "vehicle:" + e.Index);
        }

        /// <summary>True when <paramref name="p"/> (plan) lies inside any of <paramref name="boxes"/>.</summary>
        public static bool Inside(IReadOnlyList<TerrainPrism> boxes, Vector2 p)
        {
            for (int i = 0; i < boxes.Count; i++)
                if (PolygonMath.Contains(boxes[i].Footprint, p)) return true;
            return false;
        }

        /// <summary>
        /// True when a vehicle box hides <paramref name="aim"/> from <paramref name="eye"/>: the line passes through it within its height.
        /// ⭐ A box that CONTAINS either end (in plan) is the viewer's or the target's own vehicle and never hides it.
        /// </summary>
        public static bool Blocks(IReadOnlyList<TerrainPrism> boxes, Vector3 eye, Vector3 aim)
        {
            var a = new Vector2(eye.X, eye.Y);
            var b = new Vector2(aim.X, aim.Y);
            Span<float> ts = stackalloc float[8];
            Span<(float T0, float T1)> iv = stackalloc (float, float)[7];
            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if ((box.Material?.SightTransmittance ?? 0f) >= TerrainWorld.SightThreshold) continue;
                if (PolygonMath.Contains(box.Footprint, a) || PolygonMath.Contains(box.Footprint, b)) continue;
                int n = PolygonMath.InsideIntervals(box.Footprint, a, b, ts, iv);
                for (int k = 0; k < n; k++)
                {
                    float z0 = eye.Z + ((aim.Z - eye.Z) * iv[k].T0);
                    float z1 = eye.Z + ((aim.Z - eye.Z) * iv[k].T1);
                    if (MathF.Min(z0, z1) < box.TopZ && MathF.Max(z0, z1) > box.BaseZ) return true;
                }
            }
            return false;
        }
    }
}
