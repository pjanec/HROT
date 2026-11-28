using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐⭐ SD-S2b (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-108) — a drone's BATTERY: ONE state, the fraction left (1 full … 0 empty).
    /// Written by <see cref="DroneBatterySystem"/> only (the drone's brain node: the brain group, <c>HrotOwnershipGroups</c>), drained while it is AIRBORNE by
    /// <c>dt ÷ FlightParams.EnduranceSeconds</c> and frozen in pause (the system runs only on a frame that advances time — 🔒 user `2026-11-27`: <i>"drone battery
    /// must freeze"</i>). Read by the AI drones (<c>DroneReconBehavior</c>, <c>AirAttackNodes.Dive</c>: empty ⇒ down where it is) and, next, by the remote pilot
    /// and the ground station's OSD (SD-S2c / S2d). Added full at creation (<see cref="FlightTkbTranslator"/>, a drone type only — <see cref="DroneBatteries.IsBatteryDrone"/>);
    /// a scenario may author a lower start (it is saved, so the authored value survives a load). Unmanaged, recorded.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.DroneBattery)]
    public struct DroneBattery
    {
        /// <summary>The charge left, 1 = full … 0 = empty.</summary>
        public float Remaining01;
    }

    /// <summary>⭐ SD-S2b (D-108) — the one rule of who has a battery, and how a reader asks it.</summary>
    public static class DroneBatteries
    {
        /// <summary>
        /// ⭐ The unmanned-aircraft rule: a DIS type in SISO's unmanned air category (1.2.*.50) — the FPV, recon and bomber quadcopters
        /// (<c>NedTkbCatalog.DefineFpvDrone / DefineReconDrone / DefineBomberDrone</c>). ⭐ ONE rule: the radar SAM's "is it unmanned" reads it too.
        /// </summary>
        public static bool IsUnmannedAir(in DISEntityType t) => t.Kind == 1 && t.Domain == 2 && t.Category == 50;

        /// <summary>A type that gets a <see cref="DroneBattery"/> at creation: an unmanned aircraft whose flight has an endurance (a helicopter's
        /// <c>EnduranceSeconds</c> is no battery anybody flies down on, so it gets none).</summary>
        public static bool IsBatteryDrone(in DISEntityType type, FlightParamsDto flight) => flight.EnduranceSeconds > 0f && IsUnmannedAir(type);

        /// <summary>Whether <paramref name="e"/> has a battery and it is empty. No battery ⇒ false (it never runs out).</summary>
        public static bool Empty(EntityRepository world, Entity e)
            => world.IsComponentTypeRegistered<DroneBattery>() && world.HasComponent<DroneBattery>(e) && world.GetComponentRO<DroneBattery>(e).Remaining01 <= 0f;

        /// <summary>The charge left (0 … 1); 1 for an entity with no battery.</summary>
        public static float Remaining(EntityRepository world, Entity e)
            => world.IsComponentTypeRegistered<DroneBattery>() && world.HasComponent<DroneBattery>(e) ? world.GetComponentRO<DroneBattery>(e).Remaining01 : 1f;
    }

    /// <summary>
    /// ⭐⭐ SD-S2b (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-108) — the ONE writer of <see cref="DroneBattery"/>: each drone this node owns the
    /// battery of, while it is airborne (<see cref="FlightGround.OnTheGround"/> false), loses <c>dt ÷ FlightParams.EnduranceSeconds</c>, never below 0. A drone on
    /// the ground or already empty is left as it is. <see cref="TickPolicy.WhenTimeAdvances"/> ⇒ nothing drains in pause. Serial (a handful of drones, each writes
    /// only its own battery — no cross-entity read). ⛔⛔ Hot path (R-2084): the query is built once, a steady frame allocates nothing.
    /// Registered by the CGF pack (every brain host: the CGF node and the editor), before the brain tick reads it.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class DroneBatterySystem : IEcsModuleSystem
    {
        private readonly bool _gateOnAuthority;
        private EntityQuery? _drones;

        /// <param name="gateOnAuthority">Drain only the batteries this node owns (the brain group's owner) — a cluster's CGF node.</param>
        public DroneBatterySystem(bool gateOnAuthority = false) => _gateOnAuthority = gateOnAuthority;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (deltaTime <= 0f || view is not EntityRepository repo || !repo.IsComponentTypeRegistered<DroneBattery>() || !repo.IsComponentTypeRegistered<FlightParams>()) return;
            _drones ??= repo.Query().With<DroneBattery>().With<FlightParams>().Build();
            foreach (var e in _drones) Drain(repo, e, deltaTime, _gateOnAuthority);
        }

        /// <summary>One drone's step (public for the rails): airborne ⇒ the charge falls by <paramref name="dt"/> ÷ its endurance.</summary>
        public static void Drain(EntityRepository repo, Entity e, float dt, bool gateOnAuthority = false)
        {
            if (gateOnAuthority && !repo.HasAuthority<DroneBattery>(e)) return;
            float endurance = repo.GetComponentRO<FlightParams>(e).EnduranceSeconds;
            if (endurance <= 0f) return;
            float left = repo.GetComponentRO<DroneBattery>(e).Remaining01;
            if (left <= 0f || FlightGround.OnTheGround(repo, e)) return;
            repo.SetComponent(e, new DroneBattery { Remaining01 = MathF.Max(0f, left - dt / endurance) });
        }
    }
}
