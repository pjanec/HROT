using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Navigation;

namespace Bagira.SimHost.Brains
{
    /// <summary>
    /// FastBTree action node delegates for SimHost mission doctrines.
    /// </summary>
    public static class SimHostNodes
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct MoveToLocationParams
        {
            public float X;
            public float Y;
            public float Speed;
            public float ArrivalRadius;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FollowRouteParams
        {
            // Waypoints serialized as flat X0,Y0,X1,Y1,... pairs (max 16 waypoints = 32 floats).
            public float Speed;
            public bool Loop;
        }

        public static unsafe void ParseMoveToParams(string json, byte* ptr)
        {
            var p = string.IsNullOrWhiteSpace(json)
                ? default
                : JsonSerializer.Deserialize<MoveToLocationParams>(json, JsonOptions);
            Unsafe.Write(ptr, p);
        }

        public static unsafe void ParseFollowRouteParams(string json, byte* ptr)
        {
            var p = string.IsNullOrWhiteSpace(json)
                ? default
                : JsonSerializer.Deserialize<FollowRouteParams>(json, JsonOptions);
            Unsafe.Write(ptr, p);
        }

        public static unsafe NodeStatus Action_WriteMoveToChannel(
            ref BrainBlackboard blackboard,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int paramIndex)
        {
            if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
                return NodeStatus.Failure;

            MoveToLocationParams p;
            fixed (byte* src = blackboard.Memory)
                p = Unsafe.Read<MoveToLocationParams>(src);

            ref var channel = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
            if (ctx.World.HasComponent<DoctrineState>(ctx.Self))
            {
                var doctrine = ctx.World.GetComponent<DoctrineState>(ctx.Self);
                channel.DoctrineInstanceId = doctrine.InstanceId;
            }

            bool needsActivation = channel.ActiveAction != NavigationConstants.ActionIdMoveTo
                || channel.Status == NodeStatus.Failure;
            if (needsActivation)
                unchecked { channel.ActionInstanceId++; }

            channel.ActiveAction = NavigationConstants.ActionIdMoveTo;

            var moveTo = new MoveToParams
            {
                Destination  = new Vector2(p.X, p.Y),
                ArrivalRadius = p.ArrivalRadius,
                Speed        = p.Speed
            };
            fixed (byte* dst = channel.Params)
                Unsafe.Write(dst, moveTo);

            return NodeStatus.Running;
        }

        public static unsafe NodeStatus Action_WriteFollowRouteChannel(
            ref BrainBlackboard blackboard,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int paramIndex)
        {
            if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
                return NodeStatus.Failure;

            FollowRouteParams p;
            fixed (byte* src = blackboard.Memory)
                p = Unsafe.Read<FollowRouteParams>(src);

            ref var channel = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
            if (ctx.World.HasComponent<DoctrineState>(ctx.Self))
            {
                var doctrine = ctx.World.GetComponent<DoctrineState>(ctx.Self);
                channel.DoctrineInstanceId = doctrine.InstanceId;
            }

            bool needsActivation = channel.ActiveAction != NavigationConstants.ActionIdFollowRoute
                || channel.Status == NodeStatus.Failure;
            if (needsActivation)
                unchecked { channel.ActionInstanceId++; }

            channel.ActiveAction = NavigationConstants.ActionIdFollowRoute;

            var route = new FDP.Toolkit.Navigation.FollowRouteParams
            {
                TrajectoryId = 0,
                IsLooped     = (byte)(p.Loop ? 1 : 0)
            };
            fixed (byte* dst = channel.Params)
                Unsafe.Write(dst, route);

            return NodeStatus.Running;
        }

        public static NodeStatus Action_WriteJoinFormationChannel(
            ref BrainBlackboard blackboard,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int paramIndex)
        {
            if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
                return NodeStatus.Failure;

            ref var channel = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
            if (ctx.World.HasComponent<DoctrineState>(ctx.Self))
            {
                var doctrine = ctx.World.GetComponent<DoctrineState>(ctx.Self);
                channel.DoctrineInstanceId = doctrine.InstanceId;
            }

            bool needsActivation = channel.ActiveAction != NavigationConstants.ActionIdJoinFormation
                || channel.Status == NodeStatus.Failure;
            if (needsActivation)
                unchecked { channel.ActionInstanceId++; }

            channel.ActiveAction = NavigationConstants.ActionIdJoinFormation;
            return NodeStatus.Running;
        }
    }
}
