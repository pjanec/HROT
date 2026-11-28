using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;

namespace Hrot.AI.Behaviors.Devices
{
    /// <summary>
    /// ⭐ Simulated devices S1d-3 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 R6-5 / R7-3) — THE per-entity visibility rule of every device
    /// type's HUD projector (extracted from the S1d-2 rangefinder's): the device TYPE (its <see cref="DeviceInfo.Behavior"/> is the projector's — the
    /// type id stays data until <c>DeviceDefDto</c>), the row is <see cref="PossessionMode.Operated"/>, and this node holds its brain's authority
    /// (<c>HasAuthority&lt;BehaviorState&gt;</c> — the node that runs it). 0 B.
    /// </summary>
    public static class DeviceHuds
    {
        public static bool IsVisible(ISimulationView view, Entity entity, in FixedString64 behavior)
        {
            if (view is not EntityRepository w) return false;
            if (!w.GetComponentRO<DeviceInfo>(entity).Behavior.Equals(behavior)) return false;
            if (w.GetComponentRO<PossessedBy>(entity).Mode != PossessionMode.Operated) return false;
            return w.IsComponentTypeRegistered<BehaviorState>() && w.HasComponent<BehaviorState>(entity) && w.HasAuthority<BehaviorState>(entity);
        }
    }
}
