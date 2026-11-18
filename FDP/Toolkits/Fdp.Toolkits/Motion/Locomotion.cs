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
            LocomotionClass.Quadruped => MotionDomain.Ground,   // ⭐ CE-4623 AB-2 (AR-1) — an animal on legs
            LocomotionClass.Bird => MotionDomain.Air,            // ⭐ CE-4623 AB-2 (AR-2) — moved in the air, never an aircraft (IsAircraft)
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
            VehicleClass.Quadruped => LocomotionClass.Quadruped,   // ⭐ CE-4623 AB-2
            _ => LocomotionClass.Wheeled,
        };

        /// <summary>⭐ Air Ops slice 4 — true when <paramref name="e"/> moves in the AIR domain (its <see cref="Locomotion"/> class).</summary>
        public static bool InAir(EntityRepository view, Entity e) => DomainOf(Of(view, e)) == MotionDomain.Air;

        /// <summary>⭐⭐ Airborne insertion D-4 (B-6) — an AIRCRAFT: air domain and not a man coming down a rope or under a canopy. A
        /// missile's seeker and the "aircraft overhead" rules read THIS, so a Stinger never locks a parachutist (a rifle still shoots him).</summary>
        /// <summary>⭐ <c>CE-4617</c> — on any view (the radar's candidates are read on the solver's view; a repository is one).</summary>
        public static bool IsAircraft(Fdp.ModuleHost.Abstractions.ISimulationView view, Entity e)
        {
            var c = Of(view, e);
            return DomainOf(c) == MotionDomain.Air && c is not (LocomotionClass.Rope or LocomotionClass.Parachute or LocomotionClass.Bird);
        }

        /// <summary>The body's class: ⭐ airborne insertion D-1 — a <see cref="Descent"/> FIRST (it replicates, so every node agrees), then its
        /// <see cref="Locomotion"/>, else its <see cref="VehicleParams"/> preset, else Unknown.</summary>
        public static LocomotionClass Of(Fdp.ModuleHost.Abstractions.ISimulationView view, Entity e)
        {
            if (Has<Descent>(view, e))
            {
                ref readonly var d = ref view.GetComponentRO<Descent>(e);
                if (d.Kind != DescentKind.None) return d.Class;
            }
            if (Has<Locomotion>(view, e))
            {
                var c = view.GetComponentRO<Locomotion>(e).Class;
                if (c != LocomotionClass.Unknown) return c;
            }
            if (Has<VehicleParams>(view, e))
                return FromVehicleClass(view.GetComponentRO<VehicleParams>(e).Class);
            return LocomotionClass.Unknown;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-1373</c> — <b>the ONE <c>IsVehicle</c></b>: a ground body that is not a person, read through <see cref="Of"/> (Descent, then <see cref="Locomotion"/>, then the <see cref="VehicleParams"/> preset) —
        /// so a wheeled body with a <see cref="Locomotion"/> and NO <see cref="VehicleParams"/> is a vehicle too. A body with no class anywhere keeps the old fallback: a bare <see cref="VehicleState"/> is a vehicle.
        /// <c>VehicleCover.IsVehicle</c> and <c>PathRequests.IsVehicle</c> forward here (they were two copies of the narrower rule).
        /// </summary>
        public static bool IsVehicle(Fdp.ModuleHost.Abstractions.ISimulationView view, Entity e)
        {
            var c = Of(view, e);
            if (c != LocomotionClass.Unknown) return DomainOf(c) == MotionDomain.Ground && c != LocomotionClass.Pedestrian && c != LocomotionClass.Quadruped;   // ⭐ CE-4623: an animal is no vehicle
            return Has<VehicleState>(view, e);
        }

        /// <summary>
        /// ⭐ <c>CE-4658</c> — <b>the ONE "how fast is it moving, on THIS node"</b> (m/s), lifted from <c>VehicleCover.IsStanding</c> (which now reads it) so the
        /// thermal sensor's load uses the same rule: on a node that does not own its motion, the velocity replicated to it (<see cref="Fdp.Toolkit.Replication.ReplicatedMotion"/> —
        /// CE-2313: a brain node's <see cref="VehicleState"/> is the creation copy, Speed 0); else its <see cref="VehicleState"/>.Speed; else the motion as this node knows it;
        /// else its <c>SimVelocity</c>; else 0. ⚠ A person on the node that moves him may read 0 (no velocity is kept there) — a caller that needs it measures the move.
        /// </summary>
        public static float SpeedOf(Fdp.ModuleHost.Abstractions.ISimulationView view, Entity e)
        {
            var repo = view as EntityRepository;
            if (repo != null && !repo.HasAuthority<SimTransform>(e)
                && repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkVelocity>()
                && repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkVelocity>(e)
                && Fdp.Toolkit.Replication.ReplicatedMotion.Of(repo, e, out _, out var replicated))
                return replicated.Length();
            if ((repo == null || repo.IsComponentTypeRegistered<VehicleState>()) && view.HasComponent<VehicleState>(e))
                return System.MathF.Abs(view.GetComponentRO<VehicleState>(e).Speed);
            if (repo != null && Fdp.Toolkit.Replication.ReplicatedMotion.Of(repo, e, out _, out var velocity))
                return velocity.Length();
            if (repo == null && view.HasComponent<SimVelocity>(e))
                return view.GetComponentRO<SimVelocity>(e).Linear.Length();
            return 0f;
        }

        // a component read through the view: an unregistered type is "absent" (the repository answers by registration; any other view by its own HasComponent)
        private static bool Has<T>(Fdp.ModuleHost.Abstractions.ISimulationView view, Entity e) where T : unmanaged
            => (view is not EntityRepository repo || repo.IsComponentTypeRegistered<T>()) && view.HasComponent<T>(e);
    }
}
