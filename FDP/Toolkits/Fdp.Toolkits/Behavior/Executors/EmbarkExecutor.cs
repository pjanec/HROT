using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Behavior.Executors
{
    /// <summary>
    /// Parameters written into <see cref="InteractionChannel.Params"/> when requesting an embark.
    /// Must be &lt;= <see cref="BehaviorConstants.ActionParamsByteSize"/> bytes.
    /// </summary>
    public struct EmbarkParams
    {
        /// <summary>The vehicle entity the soldier wishes to board.</summary>
        public Entity VehicleEntity;

        /// <summary>
        /// Maximum distance (metres) from the soldier to the vehicle for boarding to succeed.
        /// Default: 3.0 m.
        /// </summary>
        public float MaxBoardingRange;

        /// <summary>
        /// ⭐ <c>CE-2173</c> slice 3 (B10, Air Operations §9) — who boards: <see cref="Entity.Null"/> ⇒ the entity running the action; else
        /// ANOTHER man (a casualty the runner has just put down beside the vehicle — <c>LoadCasualty</c>), whose position is the one
        /// checked against <see cref="MaxBoardingRange"/>. Appended: a zero field keeps every existing caller as it was.
        /// </summary>
        public Entity Passenger;

        /// <summary>
        /// ⭐ <c>CE-2176</c> (Weapon Systems §4b <c>ManWeapon</c>, W-B) — the seat role to board: <see cref="Components.SeatRole.Passenger"/>
        /// (0, every existing caller) or <see cref="Components.SeatRole.Gunner"/> to MAN a crew weapon — 📐 measured live: a plain board
        /// into a two-man mortar took the loader's (passenger) seat, so nobody was in its Gunner seat and nobody controlled it.
        /// Appended.
        /// </summary>
        public Components.SeatRole Role;
    }

    /// <summary>
    /// ⭐ <c>CE-3110</c> I-2a — the soldier's <c>Embark</c> interaction action (<see cref="BehaviorConstants.ActionIdEmbark"/>): close
    /// the distance, then RAISE <see cref="Events.EmbarkEntityCommand"/> — the vehicle's owner applies it (<c>EmbarkationSystem</c>, through
    /// <see cref="Embarkation"/>) — and wait for the answer, which is the soldier's own <see cref="IsEmbarkedTag"/>.
    /// <list type="number">
    ///   <item>The vehicle gone ⇒ <c>Failure</c>; already aboard it ⇒ <c>Success</c>.</item>
    ///   <item>Farther than <see cref="EmbarkParams.MaxBoardingRange"/> ⇒ <c>Running</c> (locomotion is closing the distance).</item>
    ///   <item>No free seat (as this node sees the list) ⇒ <c>Failure</c>; else raise the command once.</item>
    ///   <item>No tag within <see cref="ResultTimeoutSeconds"/> ⇒ <c>Failure</c> (the owner refused).</item>
    /// </list>
    /// ⛔ It used to write the vehicle's list, the tag and the capabilities itself — on the SOLDIER's node, whoever owned the vehicle.
    /// 📄 docs/DESIGN_Entity_Interactions.md §7.
    /// </summary>
    public class EmbarkExecutor : IActionExecutor<InteractionChannel>
    {
        /// <summary>How long the soldier waits for the owner's answer after raising the command (s).</summary>
        public const float ResultTimeoutSeconds = 5f;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct EmbarkState
        {
            public float SinceSent;
            public byte Sent;
        }

        /// <inheritdoc/>
        public unsafe void OnEnter(Entity entity, ref InteractionChannel channel, EntityRepository world)
        {
            fixed (byte* p = channel.State) *(EmbarkState*)p = default;
            channel.Status = NodeStatus.Running;
        }

        /// <inheritdoc/>
        public unsafe void Execute(Entity entity, ref InteractionChannel channel, EntityRepository world, float dt)
        {
            ref readonly var p = ref Unsafe.As<byte, EmbarkParams>(ref channel.Params[0]);
            if (!world.IsAlive(p.VehicleEntity) || !world.HasComponent<PassengerBuffer>(p.VehicleEntity)) { channel.Status = NodeStatus.Failure; return; }
            var who = p.Passenger.IsNull ? entity : p.Passenger;   // ⭐ CE-2173 — self, or the casualty being loaded
            if (!world.IsAlive(who)) { channel.Status = NodeStatus.Failure; return; }
            if (world.HasComponent<IsEmbarkedTag>(who) && world.GetComponentRO<IsEmbarkedTag>(who).VehicleEntity == p.VehicleEntity)
            {
                channel.Status = NodeStatus.Success;
                return;
            }

            EmbarkState s;
            fixed (byte* sp = channel.State) s = *(EmbarkState*)sp;
            if (s.Sent != 0)
            {
                channel.Status = (s.SinceSent += dt) > ResultTimeoutSeconds ? NodeStatus.Failure : NodeStatus.Running;
            }
            else
            {
                float range = p.MaxBoardingRange > 0f ? p.MaxBoardingRange : 3f;
                // ⭐ CE-2225 — the range is the LOADER's: a casualty being loaded (who ≠ entity) cannot walk, and PutDown lays him a
                //   body-length from his carrier — measured from him, a man put down just outside the range waited here for ever
                //   (live ua-air-rescue: the pilot on the ground 5 m from the UH-60, his medic standing by). Self-embark: who == entity.
                var soldierPos = world.GetComponent<SimTransform>(entity).Position;
                var vehiclePos = world.GetComponent<SimTransform>(p.VehicleEntity).Position;
                if (Vector3.Distance(soldierPos, vehiclePos) > range) { channel.Status = NodeStatus.Running; return; }
                if (Embarkation.FreeSeat(world, p.VehicleEntity, p.Role) < 0)   // ⭐ CE-2151 — no free seat of the type's (CE-2176: of the role)
                    { channel.Status = NodeStatus.Failure; return; }
                world.Bus.Publish(new Events.EmbarkEntityCommand { Passenger = who, Vehicle = p.VehicleEntity, Role = p.Role });
                s.Sent = 1;
                channel.Status = NodeStatus.Running;
            }
            fixed (byte* sp = channel.State) *(EmbarkState*)sp = s;
        }

        /// <inheritdoc/>
        public void OnExit(Entity entity, ref InteractionChannel channel, EntityRepository world) { }
    }
}
