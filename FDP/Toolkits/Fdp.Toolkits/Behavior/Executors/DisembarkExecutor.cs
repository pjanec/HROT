using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Executors
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d — the PASSENGER's <c>Disembark</c> interaction action (<see cref="BehaviorConstants.ActionIdDisembark"/>):
    /// RAISES <see cref="Events.DisembarkEntityCommand"/> once (the vehicle its own <see cref="IsEmbarkedTag"/> names) and waits for the
    /// answer — the tag gone. The vehicle's owner (<c>EmbarkationSystem</c>, through <see cref="Embarkation.Disembark"/>) stands it beside
    /// the hull at its seat (CE-321) and gives back its capabilities. Not aboard ⇒ <c>Success</c> at once; no answer within
    /// <see cref="ResultTimeoutSeconds"/> ⇒ <c>Failure</c>. 📄 docs/DESIGN_Entity_Interactions.md §7.3.
    /// <para>⭐ It runs aboard: boarding strips only CanMove / CanShoot, never CanInteract (<see cref="Embarkation.StrippedAboard"/>).</para>
    /// </summary>
    public class DisembarkExecutor : IActionExecutor<InteractionChannel>
    {
        /// <summary>How long the passenger waits for the owner's answer after raising the command (s).</summary>
        public const float ResultTimeoutSeconds = 5f;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct DisembarkState
        {
            public float SinceSent;
            public byte Sent;
        }

        /// <inheritdoc/>
        public unsafe void OnEnter(Entity entity, ref InteractionChannel channel, EntityRepository world)
        {
            fixed (byte* p = channel.State) *(DisembarkState*)p = default;
            channel.Status = NodeStatus.Running;
        }

        /// <inheritdoc/>
        public unsafe void Execute(Entity entity, ref InteractionChannel channel, EntityRepository world, float dt)
        {
            if (!world.HasComponent<IsEmbarkedTag>(entity)) { channel.Status = NodeStatus.Success; return; }
            DisembarkState s;
            fixed (byte* p = channel.State) s = *(DisembarkState*)p;
            if (s.Sent == 0)
            {
                world.Bus.Publish(new Events.DisembarkEntityCommand
                {
                    Passenger = entity, Vehicle = world.GetComponentRO<IsEmbarkedTag>(entity).VehicleEntity,
                });
                s.Sent = 1;
                channel.Status = NodeStatus.Running;
            }
            else channel.Status = (s.SinceSent += dt) > ResultTimeoutSeconds ? NodeStatus.Failure : NodeStatus.Running;
            fixed (byte* p = channel.State) *(DisembarkState*)p = s;
        }

        /// <inheritdoc/>
        public void OnExit(Entity entity, ref InteractionChannel channel, EntityRepository world) { }
    }
}
