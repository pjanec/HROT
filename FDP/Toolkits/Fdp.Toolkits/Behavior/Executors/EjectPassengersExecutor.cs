using System.Numerics;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Behavior.Executors
{
    /// <summary>
    /// ⭐ <c>CE-3110</c> I-2a — the vehicle's <c>EjectPassengers</c> interaction action (<see cref="BehaviorConstants.ActionIdEjectPassengers"/>):
    /// RAISES <see cref="Events.EjectPassengersCommand"/> once and waits for the answer — the vehicle's own <see cref="PassengerBuffer"/>
    /// empty. The vehicle's owner (<c>EmbarkationSystem</c>, through <see cref="Embarkation.EjectAll"/>) puts every live passenger in a
    /// column beside the hull (CE-321) and gives back their capabilities. ⛔ It used to do all of that itself, on whichever node ran it.
    /// 📄 docs/DESIGN_Entity_Interactions.md §7.
    /// </summary>
    public class EjectPassengersExecutor : IActionExecutor<InteractionChannel>
    {
        /// <inheritdoc cref="Embarkation.DefaultHullRadius"/>
        public const float DefaultHullRadius = Embarkation.DefaultHullRadius;
        /// <inheritdoc cref="Embarkation.DismountClearance"/>
        public const float DismountClearance = Embarkation.DismountClearance;
        /// <inheritdoc cref="Embarkation.DismountSpacing"/>
        public const float DismountSpacing = Embarkation.DismountSpacing;
        /// <summary>How long the vehicle waits for the owner's answer (s).</summary>
        public const float ResultTimeoutSeconds = 5f;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct EjectState
        {
            public float SinceSent;
            public byte Sent;
        }

        /// <inheritdoc/>
        public unsafe void OnEnter(Entity entity, ref InteractionChannel channel, EntityRepository world)
        {
            fixed (byte* p = channel.State) *(EjectState*)p = default;
            channel.Status = NodeStatus.Running;
        }

        /// <inheritdoc/>
        public unsafe void Execute(Entity entity, ref InteractionChannel channel, EntityRepository world, float dt)
        {
            if (!world.HasComponent<PassengerBuffer>(entity)) { channel.Status = NodeStatus.Failure; return; }
            if (world.GetComponentRO<PassengerBuffer>(entity).Count == 0) { channel.Status = NodeStatus.Success; return; }
            EjectState s;
            fixed (byte* p = channel.State) s = *(EjectState*)p;
            if (s.Sent == 0)
            {
                world.Bus.Publish(new Events.EjectPassengersCommand { Vehicle = entity });
                s.Sent = 1;
                channel.Status = NodeStatus.Running;
            }
            else channel.Status = (s.SinceSent += dt) > ResultTimeoutSeconds ? NodeStatus.Failure : NodeStatus.Running;
            fixed (byte* p = channel.State) *(EjectState*)p = s;
        }

        /// <inheritdoc/>
        public void OnExit(Entity entity, ref InteractionChannel channel, EntityRepository world) { }
    }
}
