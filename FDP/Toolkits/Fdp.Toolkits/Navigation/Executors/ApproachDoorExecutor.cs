using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;

namespace Fdp.Toolkit.Navigation.Executors
{
    /// <summary>
    /// ⭐ <c>Q94 C</c> (R-3018) — the params of <see cref="NavigationConstants.ActionIdApproachDoor"/>: WHICH door (its network id) and
    /// its centre (the fallback point where the door is not known). 28 of the channel's 32 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ApproachDoorParams
    {
        public long DoorNetworkId;
        public Vector3 DoorCentre;
        public float Speed;
        public float ArrivalRadius;
    }

    /// <summary>
    /// ⭐ <c>Q94 C</c> (R-3018, 📄 <c>docs/blueprints/Architect_Question_94_Brain_Geometry_Sites.md</c> §5.1) — walk to a door: the brain
    /// says WHICH door, the muscle decides WHICH SIDE. Writes a path-to-point intent whose <see cref="NavigationIntent.RouteEntityId"/>
    /// names the door; <see cref="NavigationDestination.Of"/> on the muscle turns it into the near side of the wall. Status and stop
    /// are exactly a <see cref="MoveToExecutor"/>'s (one implementation, delegated).
    /// </summary>
    public sealed class ApproachDoorExecutor : IActionExecutor<LocomotionChannel>
    {
        private readonly MoveToExecutor _move = new();

        public unsafe void OnEnter(Entity entity, ref LocomotionChannel channel, EntityRepository world)
        {
            ApproachDoorParams p;
            fixed (byte* src = channel.Params)
                p = *(ApproachDoorParams*)src;

            var intent = world.GetComponent<NavigationIntent>(entity);
            intent.IntentId++;
            intent.Mode             = NavigationMode.PathToPoint;
            intent.FinalDestination = p.DoorCentre;
            intent.RouteEntityId    = p.DoorNetworkId;
            intent.TargetSpeed      = p.Speed;
            intent.ArrivalRadius    = p.ArrivalRadius;
            intent.ReverseAllowed   = 0;
            intent.Flags            = 0;
            intent.MaxReplans       = 0;
            intent.RouteHandle      = 0;
            intent.LayerMask        = 0;
            intent.BackendForce     = 0;
            intent.ReplanTimeBudget = 0f;
            world.SetComponent(entity, intent);

            channel.Status = NodeStatus.Running;
        }

        public void Execute(Entity entity, ref LocomotionChannel channel, EntityRepository world, float dt)
            => _move.Execute(entity, ref channel, world, dt);

        public void OnExit(Entity entity, ref LocomotionChannel channel, EntityRepository world)
            => _move.OnExit(entity, ref channel, world);
    }
}
