using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Executors
{
    /// <summary>
    /// ⭐ Airborne insertion B-9 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §7d) — an aircraft's <c>Eject</c> interaction action
    /// (<see cref="BehaviorConstants.ActionIdEject"/>): RAISES <see cref="Events.EjectRequestEvent"/> once and succeeds when the aircraft is
    /// abandoned (its Health at 0 — <c>EjectionSystem</c> spawns the pilot first, then ends it). ⛔ It spawns nothing itself: one spawn site
    /// (D-14). Fails after <see cref="ResultTimeoutSeconds"/> with no answer (no ejection seat, or a type with no pilot).
    /// </summary>
    public sealed class EjectExecutor : IActionExecutor<InteractionChannel>
    {
        /// <summary>How long the aircraft waits for the ejection (s).</summary>
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
            if (Abandoned(world, entity)) { channel.Status = NodeStatus.Success; return; }
            EjectState s;
            fixed (byte* p = channel.State) s = *(EjectState*)p;
            if (s.Sent == 0)
            {
                if (world.Bus.IsRegistered<Events.EjectRequestEvent>()) world.Bus.Publish(new Events.EjectRequestEvent { Aircraft = entity });
                s.Sent = 1;
                channel.Status = NodeStatus.Running;
            }
            else channel.Status = (s.SinceSent += dt) > ResultTimeoutSeconds ? NodeStatus.Failure : NodeStatus.Running;
            fixed (byte* p = channel.State) *(EjectState*)p = s;
        }

        /// <inheritdoc/>
        public void OnExit(Entity entity, ref InteractionChannel channel, EntityRepository world) { }

        private static bool Abandoned(EntityRepository world, Entity aircraft)
            => !world.IsAlive(aircraft)
               || (world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() && world.HasComponent<Fdp.Toolkit.Combat.Components.Health>(aircraft)
                   && world.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(aircraft).Current <= 0f);
    }
}
