using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> M-5 (IM-2, MV-9; 📄 docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §5 / §9) — <c>ParkedBomb</c>, run by the VEHICLE
    /// (a motorcycle, a car): ride to the spot, stop, arm the cargo charge's timer (<see cref="ChargeTimers.Arm"/> — a follow-up inside the
    /// parallel phase) and put the riders out (<see cref="EjectPassengersCommand"/>, applied by the vehicle's owner). The charge goes off on its
    /// timer through <c>Charges.Ask(charge, Null)</c> (<c>ChargeSystem</c>) after the riders have left. Reuses: the move channel, the charge, the
    /// eject command — no new mechanism.
    /// </summary>
    public static class ParkedBombBehavior
    {
        public const float DefaultDelaySeconds = 30f;
        public const float DefaultSpeed = 12f;
        public const float ArrivalRadius = 4f;

        [StructLayout(LayoutKind.Sequential)]
        public struct ParkedBombParams
        {
            /// <summary>The charge's network id (resolved each tick: a load re-allocates handles).</summary>
            public int Charge;
            public Vector3 Spot;
            public SimDuration Delay;
            public float Speed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ParkedBombState
        {
            /// <summary>1 once the ride to the spot is ordered.</summary>
            public byte Riding;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ParkedBombBlackboard
        {
            public ParkedBombParams Params;
            public ParkedBombState State;
        }

        [BehaviorResolver(BehaviorNames.ParkedBomb)]
        public static void Resolve(in ParkedBombParamsJsonDto authored, ref ParkedBombBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new ParkedBombParams
            {
                Charge = authored.Charge.Ref.NetworkId,
                Spot = authored.Spot,
                Delay = (float)authored.Delay > 0f ? authored.Delay : (SimDuration)DefaultDelaySeconds,
                Speed = authored.Speed > 0f ? authored.Speed : DefaultSpeed,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.ParkedBomb, Params = typeof(ParkedBombParamsJsonDto), Categories = BehaviorCategory.Civilian | BehaviorCategory.AllMilitary,
            Description = "Rides its cargo bomb to a spot, parks, sets the timer and puts the riders out.")]
        [BTreeDefinition(BehaviorNames.ParkedBomb, Curated = true, Group = "Library/Casualties and engineering", ParamsType = typeof(ParkedBombBlackboard))]
        public static BTreeBuilder<ParkedBombBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ParkedBombBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Tick);

        /// <summary>One tick: ride to the spot; there, stop, arm and put the riders out ⇒ Success.</summary>
        public static NodeStatus Tick(ref ParkedBombParams p, ref ParkedBombState s, Entity self, EntityRepository world)
        {
            var charge = new Fdp.Toolkit.Replication.EntityRef(p.Charge).Resolve(world);
            if (charge.IsNull || !Charges.IsCharge(world, charge)) return NodeStatus.Failure;
            if (s.Riding == 0)
            {
                if (!LocomotionMoveTo.Issue(world, self, p.Spot, p.Speed, ArrivalRadius, MoveGait.Keep, source: LocomotionMoveTo.OrderSource(world, self)))
                    return NodeStatus.Failure;
                s.Riding = 1;
                return NodeStatus.Running;
            }
            var move = LocomotionMoveTo.Status(world, self);
            if (move == NodeStatus.Running) return NodeStatus.Running;
            if (move == NodeStatus.Failure) return NodeStatus.Failure;
            LocomotionMoveTo.Stop(world, self);
            if (!ChargeTimers.Arm(world, charge, p.Delay)) return NodeStatus.Failure;
            if (world.Bus.IsRegistered<EjectPassengersCommand>()) world.Bus.Publish(new EjectPassengersCommand { Vehicle = self });
            return NodeStatus.Success;
        }
    }
}
