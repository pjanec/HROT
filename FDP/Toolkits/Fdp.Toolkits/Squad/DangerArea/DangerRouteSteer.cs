using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// ⭐⭐ <c>CE-2469</c> SP-1 (R-2140 R3; the contract behaviors binds — HANDOFF_Behaviors_To_Backend_RouteSteerPoint.md) — where a priced path to the objective
    /// goes next: the steer point of <paramref name="commander"/>'s danger-area sensor's answer, planned on the navmesh node with the unit's side, so it goes round
    /// ground a known enemy covers. 0 B; safe from the parallel brain phase (reads the sensor child only).
    /// </summary>
    public static class DangerRouteSteer
    {
        /// <summary>An answer older than this (s) is not used.</summary>
        public const float FreshSeconds = 4f;

        /// <summary>True with the steer point when the commander's danger-area sensor holds a FRESH answer that came from a planned route.</summary>
        public static bool Of(EntityRepository world, Entity commander, out Vector3 steer)
        {
            steer = default;
            if (!world.IsComponentTypeRegistered<DangerAreaCognitiveBuffer>()) return false;
            var child = UnitSensors.Of(world, commander, SensorModality.DangerArea);
            if (child.IsNull || !world.HasComponent<DangerAreaCognitiveBuffer>(child)) return false;
            ref readonly var b = ref world.GetComponentRO<DangerAreaCognitiveBuffer>(child);
            if (b.HasSteer == 0 || !b.IsReady || ((ISimulationView)world).Time - b.LastUpdateTimeSeconds > FreshSeconds) return false;
            steer = b.SteerPoint;
            return true;
        }
    }
}
