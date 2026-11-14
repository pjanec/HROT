using Fbt;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Hrot.Editor.AiShared;

namespace Hrot.AI.Behaviors.StandardLibrary
{
    /// <summary>
    /// ⭐ <c>CE-3078</c> H7 — reads of the unit's MoveTo, for a blueprint that issues moves WITHOUT suspending on
    /// <c>WaitForChannel</c> (a behaviour that must keep reacting every tick while it walks — a wait would stop the graph at
    /// the wait). The same reading <c>LocomotionMoveTo.Status</c> gives the C# nodes. 📄 docs/DESIGN_Sensors_And_Doctrine.md §7.10b.
    /// </summary>
    public static class BlueprintLocomotionLibrary
    {
        /// <summary>True when the unit's MoveTo is active and has ARRIVED (the executor reported Success).</summary>
        [BlueprintCallable("Locomotion", DisplayName = "Move Arrived")]
        public static bool MoveArrived(Entity self, ISimulationView view) => Status(self, view) == NodeStatus.Success;

        /// <summary>True when the unit's MoveTo is active and FAILED (no path, blocked, out of re-plans).</summary>
        [BlueprintCallable("Locomotion", DisplayName = "Move Failed")]
        public static bool MoveFailed(Entity self, ISimulationView view)
            => view.HasComponent<LocomotionChannel>(self) && view.GetComponentRO<LocomotionChannel>(self).ActiveAction == NavigationConstants.ActionIdMoveTo
               && view.GetComponentRO<LocomotionChannel>(self).Status == NodeStatus.Failure;

        private static NodeStatus Status(Entity self, ISimulationView view)
        {
            if (!view.HasComponent<LocomotionChannel>(self)) return NodeStatus.Failure;
            ref readonly var channel = ref view.GetComponentRO<LocomotionChannel>(self);
            return channel.ActiveAction == NavigationConstants.ActionIdMoveTo ? channel.Status : NodeStatus.Failure;
        }
    }
}
