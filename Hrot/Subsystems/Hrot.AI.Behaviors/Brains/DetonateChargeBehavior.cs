using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2238</c> part 5 (AW-12, B5; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §5l BM-1) — <c>DetonateCharge</c>: he stands still
    /// <see cref="DetonateChargeParams.Seconds"/>, then ASKS the first charge he carries (backend B-11b, <see cref="Charges.Ask"/>, by himself) to
    /// go off; the charge's position owner bursts it where it stands (worn: at him). Success once asked; Failure when he carries no charge.
    /// ⛔ It owns no burst and never moves the charge (BM-2: a worn charge stays hidden until it goes off).
    /// </summary>
    public static class DetonateChargeBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct DetonateChargeParams
        {
            public float Seconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DetonateChargeState
        {
            /// <summary>When he began to stand still (0 = not yet).</summary>
            public double Since;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DetonateChargeBlackboard
        {
            public DetonateChargeParams Params;
            public DetonateChargeState State;
        }

        [BehaviorResolver(BehaviorNames.DetonateCharge)]
        public static void Resolve(in DetonateChargeParamsJsonDto authored, ref DetonateChargeBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new DetonateChargeParams { Seconds = authored.Seconds > 0f ? authored.Seconds : 0f };
            block.State = default;
        }

        [Behavior(BehaviorNames.DetonateCharge, Params = typeof(DetonateChargeParamsJsonDto), Categories = BehaviorCategory.AllMilitary | BehaviorCategory.Civilian)]
        [BTreeDefinition(BehaviorNames.DetonateCharge, Curated = true, Group = "Library/Orders", ParamsType = typeof(DetonateChargeBlackboard))]
        public static BTreeBuilder<DetonateChargeBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<DetonateChargeBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Set);

        /// <summary>One tick: still for <c>Seconds</c>, then ask his charge to go off. Success once asked.</summary>
        public static NodeStatus Set(ref DetonateChargeParams p, ref DetonateChargeState ws, Entity self, EntityRepository world)
        {
            var charge = ChargeOf(world, self);
            if (charge.IsNull) return NodeStatus.Failure;
            double now = world.SimulationTime > 0 ? world.SimulationTime : 1e-6;
            LocomotionMoveTo.Stop(world, self);
            if (ws.Since <= 0) ws.Since = now;
            if (now - ws.Since < p.Seconds) return NodeStatus.Running;
            return Charges.Ask(world, charge, self) ? NodeStatus.Success : NodeStatus.Failure;
        }

        /// <summary>The first charge <paramref name="man"/> carries (any station), or <see cref="Entity.Null"/>.</summary>
        internal static Entity ChargeOf(EntityRepository world, Entity man)
        {
            if (!world.HasComponent<PassengerBuffer>(man)) return Entity.Null;
            var buffer = world.GetComponentRO<PassengerBuffer>(man);
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
                if (!buffer.Passengers[i].IsNull && Charges.IsCharge(world, buffer.Passengers[i])) return buffer.Passengers[i];
            return Entity.Null;
        }
    }
}
