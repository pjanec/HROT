using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior.Executors
{
    /// <summary>
    /// Parameters written into <see cref="InteractionChannel.Params"/> when a carrier picks a casualty up.
    /// Must be &lt;= <see cref="BehaviorConstants.ActionParamsByteSize"/> bytes.
    /// </summary>
    public struct PickUpParams
    {
        /// <summary>The man to pick up (incapacitated — the rules refuse anyone else).</summary>
        public Entity Casualty;

        /// <summary>How he is taken: <see cref="CarryMode.Carried"/> or <see cref="CarryMode.Dragged"/>.</summary>
        public CarryMode Mode;

        /// <summary>How close the carrier must be to him (m); 0 ⇒ <see cref="PickUpExecutor.DefaultReach"/>.</summary>
        public float Reach;
    }

    /// <summary>
    /// ⭐ <c>CE-2152</c> C-b (C6, R-2011; 📄 docs/DESIGN_Entity_Interactions.md §7.4.10) — the CARRIER's <c>PickUp</c> interaction action
    /// (<see cref="BehaviorConstants.ActionIdPickUp"/>), the mirror of the passenger's <see cref="EmbarkExecutor"/>: in reach, RAISE
    /// <see cref="Events.EmbarkEntityCommand"/> with <c>Vehicle = self</c> — the carrier's owner applies it (<c>EmbarkationSystem</c>, through
    /// <see cref="Embarkation.TryEmbark"/> and its C3 gate) — and wait for the answer: the casualty's own <see cref="IsEmbarkedTag"/> naming self.
    /// <list type="number">
    ///   <item>The casualty gone, or not a person carrier ⇒ <c>Failure</c>; already carried by self ⇒ <c>Success</c>.</item>
    ///   <item>Carried by someone else ⇒ <c>Failure</c> (one seat, one carrier).</item>
    ///   <item>Farther than <see cref="PickUpParams.Reach"/> ⇒ <c>Running</c> (the node is walking).</item>
    ///   <item>No answer within <see cref="ResultTimeoutSeconds"/> ⇒ <c>Failure</c> (the rules refused: he is not down, or self is).</item>
    /// </list>
    /// </summary>
    public class PickUpExecutor : IActionExecutor<InteractionChannel>
    {
        /// <summary>How long the carrier waits for the owner's answer after raising the command (s).</summary>
        public const float ResultTimeoutSeconds = 5f;

        /// <summary>The reach when the params give none (m): two soldiers' colliders and a hand.</summary>
        public const float DefaultReach = 1.5f;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct PickUpState
        {
            public float SinceSent;
            public byte Sent;
        }

        /// <inheritdoc/>
        public unsafe void OnEnter(Entity entity, ref InteractionChannel channel, EntityRepository world)
        {
            fixed (byte* p = channel.State) *(PickUpState*)p = default;
            channel.Status = NodeStatus.Running;
        }

        /// <inheritdoc/>
        public unsafe void Execute(Entity entity, ref InteractionChannel channel, EntityRepository world, float dt)
        {
            ref readonly var p = ref Unsafe.As<byte, PickUpParams>(ref channel.Params[0]);
            if (!world.IsAlive(p.Casualty) || !Embarkation.IsPersonCarrier(world, entity)) { channel.Status = NodeStatus.Failure; return; }
            if (world.HasComponent<IsEmbarkedTag>(p.Casualty))
            {
                channel.Status = world.GetComponentRO<IsEmbarkedTag>(p.Casualty).VehicleEntity == entity ? NodeStatus.Success : NodeStatus.Failure;
                return;
            }

            PickUpState s;
            fixed (byte* sp = channel.State) s = *(PickUpState*)sp;
            if (s.Sent != 0)
            {
                channel.Status = (s.SinceSent += dt) > ResultTimeoutSeconds ? NodeStatus.Failure : NodeStatus.Running;
            }
            else
            {
                float reach = p.Reach > 0f ? p.Reach : DefaultReach;
                var at = world.GetComponent<SimTransform>(entity).Position;
                var him = world.GetComponent<SimTransform>(p.Casualty).Position;
                if (Vector3.Distance(at, him) > reach) { channel.Status = NodeStatus.Running; return; }
                world.Bus.Publish(new Events.EmbarkEntityCommand { Passenger = p.Casualty, Vehicle = entity, Mode = p.Mode });
                s.Sent = 1;
                channel.Status = NodeStatus.Running;
            }
            fixed (byte* sp = channel.State) *(PickUpState*)sp = s;
        }

        /// <inheritdoc/>
        public void OnExit(Entity entity, ref InteractionChannel channel, EntityRepository world) { }
    }

    /// <summary>
    /// ⭐ <c>CE-2152</c> C-b (C6) — the CARRIER's <c>PutDown</c> interaction action (<see cref="BehaviorConstants.ActionIdPutDown"/>), the mirror
    /// of the passenger's <see cref="DisembarkExecutor"/>: RAISE <see cref="Events.DisembarkEntityCommand"/> once for the man it carries
    /// (<see cref="Embarkation.LoadOf"/>, <c>Vehicle = self</c>) and wait for its list to empty. The owner lays him where he is held
    /// (<see cref="Embarkation.LeavePoint"/>) and gives the carrier his fire back. Carrying nobody ⇒ <c>Success</c> at once; no answer within
    /// <see cref="ResultTimeoutSeconds"/> ⇒ <c>Failure</c>.
    /// </summary>
    public class PutDownExecutor : IActionExecutor<InteractionChannel>
    {
        /// <summary>How long the carrier waits for the owner's answer after raising the command (s).</summary>
        public const float ResultTimeoutSeconds = 5f;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct PutDownState
        {
            public float SinceSent;
            public byte Sent;
        }

        /// <inheritdoc/>
        public unsafe void OnEnter(Entity entity, ref InteractionChannel channel, EntityRepository world)
        {
            fixed (byte* p = channel.State) *(PutDownState*)p = default;
            channel.Status = NodeStatus.Running;
        }

        /// <inheritdoc/>
        public unsafe void Execute(Entity entity, ref InteractionChannel channel, EntityRepository world, float dt)
        {
            // ⭐ CE-1207 (§8m PK-5, PK-6) — the named load (a dropped weapon), else the BODY he carries; a weapon in his hands is not "the load"
            ref readonly var named = ref Unsafe.As<byte, PutDownParams>(ref channel.Params[0]);
            var load = !named.Load.IsNull ? named.Load : Possession.OperatorUseActions.CarriedBody(world, entity);
            if (load.IsNull || !world.IsAlive(load) || !world.HasComponent<IsEmbarkedTag>(load)
                || world.GetComponentRO<IsEmbarkedTag>(load).VehicleEntity != entity) { channel.Status = NodeStatus.Success; return; }
            PutDownState s;
            fixed (byte* p = channel.State) s = *(PutDownState*)p;
            if (s.Sent == 0)
            {
                world.Bus.Publish(new Events.DisembarkEntityCommand { Passenger = load, Vehicle = entity });
                s.Sent = 1;
                channel.Status = NodeStatus.Running;
            }
            else channel.Status = (s.SinceSent += dt) > ResultTimeoutSeconds ? NodeStatus.Failure : NodeStatus.Running;
            fixed (byte* p = channel.State) *(PutDownState*)p = s;
        }

        /// <inheritdoc/>
        public void OnExit(Entity entity, ref InteractionChannel channel, EntityRepository world) { }
    }

    /// <summary>⭐ <c>CE-1207</c> (§8m PK-6) — what <see cref="PutDownExecutor"/> puts down: <see cref="Load"/>, or (null) the body he carries.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct PutDownParams
    {
        public Entity Load;
    }

    /// <summary>⭐ <c>CE-1207</c> (§8m PK-4) — the round <see cref="PickUpRoundExecutor"/> asks for.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct PickUpRoundParams
    {
        public Entity Round;
    }

    /// <summary>
    /// ⭐ <c>CE-1207</c> (docs/DESIGN_Entity_Control.md §8m PK-4, R-1007) — a man picks a lying stone / round up
    /// (<see cref="BehaviorConstants.ActionIdPickUpRound"/>): <see cref="Fdp.Toolkit.Combat.RoundPickUp.Ask"/> ONCE (the round's owner grants
    /// one man, Civilians §7.10 PU-5), then <c>Success</c> when his hands hold one more of it; a refused ask ⇒ <c>Failure</c>, and no grant
    /// within <see cref="GrantTimeoutSeconds"/> (another man was faster, or the round is gone) ⇒ <c>Failure</c>.
    /// </summary>
    public class PickUpRoundExecutor : IActionExecutor<InteractionChannel>
    {
        /// <summary>How long the asker waits for the grant to land in his hands (s).</summary>
        public const float GrantTimeoutSeconds = 2f;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct PickUpRoundState
        {
            public long Ammo;
            public int HeldBefore;
            public float Since;
            public byte Asked;
        }

        /// <inheritdoc/>
        public unsafe void OnEnter(Entity entity, ref InteractionChannel channel, EntityRepository world)
        {
            fixed (byte* p = channel.State) *(PickUpRoundState*)p = default;
            channel.Status = NodeStatus.Running;
        }

        /// <inheritdoc/>
        public unsafe void Execute(Entity entity, ref InteractionChannel channel, EntityRepository world, float dt)
        {
            ref readonly var p = ref Unsafe.As<byte, PickUpRoundParams>(ref channel.Params[0]);
            PickUpRoundState s;
            fixed (byte* sp = channel.State) s = *(PickUpRoundState*)sp;
            if (s.Asked == 0)
            {
                s.Ammo = Fdp.Toolkit.Combat.RoundPickUp.AmmoOf(world, p.Round);
                s.HeldBefore = Fdp.Toolkit.Combat.PersonHands.Rounds(world, entity, s.Ammo);
                if (s.Ammo == 0 || !Fdp.Toolkit.Combat.RoundPickUp.Ask(world, entity, p.Round)) { channel.Status = NodeStatus.Failure; return; }
                s.Asked = 1;
                channel.Status = NodeStatus.Running;
            }
            else if (Fdp.Toolkit.Combat.PersonHands.Rounds(world, entity, s.Ammo) > s.HeldBefore) channel.Status = NodeStatus.Success;
            else channel.Status = (s.Since += dt) > GrantTimeoutSeconds ? NodeStatus.Failure : NodeStatus.Running;
            fixed (byte* sp = channel.State) *(PickUpRoundState*)sp = s;
        }

        /// <inheritdoc/>
        public void OnExit(Entity entity, ref InteractionChannel channel, EntityRepository world) { }
    }
}
