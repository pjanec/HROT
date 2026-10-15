using System.Runtime.InteropServices;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>⭐ CE-2168 (Motion Domains MD-B) — the module family that moves a body; one module (and, MD-B, one ownership role) each.</summary>
    public enum MotionDomain : byte
    {
        None = 0,
        /// <summary>People, wheeled, tracked, amphibious — <c>GroundKinematicsModule</c>.</summary>
        Ground = 1,
        /// <summary>Rotorcraft, fixed-wing — <c>AirMotionModule</c>.</summary>
        Air = 2,
        /// <summary>Surface vessels — later.</summary>
        Naval = 3,
    }

    /// <summary>
    /// ⭐ CE-2168 (MD-A) — the body's <see cref="LocomotionClass"/>, stamped from the TKB's <c>Gen.Locomotion</c> by
    /// <see cref="LocomotionTkbTranslator"/> on every node: the motion modules route by it, and the FPS station picks its control
    /// scheme by it (MD-D).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Locomotion)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct Locomotion
    {
        public LocomotionClass Class;
    }

    /// <summary>⭐ CE-2168 — the class → domain rule, and a body's class read the one way.</summary>
    public static class Locomotions
    {
        public static MotionDomain DomainOf(LocomotionClass c) => c switch
        {
            LocomotionClass.Pedestrian or LocomotionClass.Wheeled or LocomotionClass.Tracked or LocomotionClass.Amphibious => MotionDomain.Ground,
            LocomotionClass.Rotorcraft or LocomotionClass.FixedWing => MotionDomain.Air,
            LocomotionClass.Rope or LocomotionClass.Parachute => MotionDomain.Air,   // ⭐ airborne insertion D-2 — moved by AirMotionSystem
            LocomotionClass.Surface => MotionDomain.Naval,
            _ => MotionDomain.None,
        };

        /// <summary>The ground class a <see cref="VehicleClass"/> preset implies (a type built before CE-2168).</summary>
        public static LocomotionClass FromVehicleClass(VehicleClass v) => v switch
        {
            VehicleClass.Pedestrian => LocomotionClass.Pedestrian,
            VehicleClass.Tank => LocomotionClass.Tracked,
            _ => LocomotionClass.Wheeled,
        };

        /// <summary>⭐ Air Ops slice 4 — true when <paramref name="e"/> moves in the AIR domain (its <see cref="Locomotion"/> class).</summary>
        public static bool InAir(EntityRepository view, Entity e) => DomainOf(Of(view, e)) == MotionDomain.Air;

        /// <summary>⭐⭐ Airborne insertion D-4 (B-6) — an AIRCRAFT: air domain and not a man coming down a rope or under a canopy. A
        /// missile's seeker and the "aircraft overhead" rules read THIS, so a Stinger never locks a parachutist (a rifle still shoots him).</summary>
        public static bool IsAircraft(EntityRepository view, Entity e)
        {
            var c = Of(view, e);
            return DomainOf(c) == MotionDomain.Air && c is not (LocomotionClass.Rope or LocomotionClass.Parachute);
        }

        /// <summary>The body's class: ⭐ airborne insertion D-1 — a <see cref="Descent"/> FIRST (it replicates, so every node agrees), then its
        /// <see cref="Locomotion"/>, else its <see cref="VehicleParams"/> preset, else Unknown.</summary>
        public static LocomotionClass Of(EntityRepository view, Entity e)
        {
            if (view.IsComponentTypeRegistered<Descent>() && view.HasComponent<Descent>(e))
            {
                ref readonly var d = ref view.GetComponentRO<Descent>(e);
                if (d.Kind != DescentKind.None) return d.Class;
            }
            if (view.IsComponentTypeRegistered<Locomotion>() && view.HasComponent<Locomotion>(e))
            {
                var c = view.GetComponentRO<Locomotion>(e).Class;
                if (c != LocomotionClass.Unknown) return c;
            }
            if (view.IsComponentTypeRegistered<VehicleParams>() && view.HasComponent<VehicleParams>(e))
                return FromVehicleClass(view.GetComponentRO<VehicleParams>(e).Class);
            return LocomotionClass.Unknown;
        }
    }
}
