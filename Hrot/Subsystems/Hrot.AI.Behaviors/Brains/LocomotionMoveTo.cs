using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fbt.Kernel;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2092</c> — the ONE way a C# node moves its own unit: a <see cref="NavigationConstants.ActionIdMoveTo"/> on the
    /// unit's <see cref="LocomotionChannel"/>, which <c>MoveToExecutor</c> turns into a pathed move (<c>PathToPoint</c>,
    /// CE-3026). Factored out of <see cref="EqsCombatNodes.Action_MoveToOptimalCover"/> so the tactics nodes do not carry a
    /// second copy of the channel write. 📄 <c>docs/DESIGN_Eqs_Consuming_Behaviours.md</c> §2.1.
    /// </summary>
    public static class LocomotionMoveTo
    {
        /// <summary>Stamps the channel with the unit's running behaviour, so arbitration does not stomp the command.</summary>
        public static void StampOwner(EntityRepository world, Entity self, ref LocomotionChannel channel)
        {
            if (world.HasComponent<BehaviorState>(self))
                channel.BehaviorInstanceId = world.GetComponent<BehaviorState>(self).InstanceId;
        }

        /// <summary>Issues a NEW MoveTo to <paramref name="destination"/> (a new action instance, so the executor re-enters).
        /// False when the unit has no locomotion channel.</summary>
        public static unsafe bool Issue(EntityRepository world, Entity self, Vector3 destination, float speed, float arrivalRadius)
        {
            if (!world.HasComponent<LocomotionChannel>(self)) return false;
            ref var channel = ref world.GetComponentRW<LocomotionChannel>(self);
            StampOwner(world, self, ref channel);
            unchecked { channel.ActionInstanceId++; }
            channel.ActiveAction = NavigationConstants.ActionIdMoveTo;
            channel.Status = NodeStatus.Running;
            var moveToParams = new MoveToParams
            {
                Destination    = destination,
                ArrivalRadius  = arrivalRadius,
                Speed          = speed,
                ReverseAllowed = 0,
            };
            fixed (byte* dst = channel.Params)
            {
                *(MoveToParams*)dst = moveToParams;
            }
            return true;
        }

        /// <summary>The status of the unit's MoveTo: Running / Success (arrived) / Failure; Failure when no MoveTo is active.</summary>
        public static NodeStatus Status(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<LocomotionChannel>(self)) return NodeStatus.Failure;
            ref readonly var channel = ref world.GetComponentRO<LocomotionChannel>(self);
            return channel.ActiveAction == NavigationConstants.ActionIdMoveTo ? channel.Status : NodeStatus.Failure;
        }
    }
}
