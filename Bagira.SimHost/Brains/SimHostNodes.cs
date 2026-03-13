using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using CarKinem.Core;
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;
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

        private class MoveToLocationParamsJsonDto
        {
            public double TargetLat { get; set; }
            public double TargetLon { get; set; }
            public float Speed { get; set; }
            public float ArrivalRadius { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
        }

        /// <summary>
        /// Fallback travel speed (m/s) applied when a <c>MoveToLocation</c> params JSON
        /// does not carry an explicit <c>speed</c> field (e.g. legacy plans committed
        /// before the field was added).  Prevents a zero-speed command that would cause
        /// the entity to stand still indefinitely.
        /// </summary>
        private const float DefaultMoveToSpeed = 15f;

        public static unsafe void ParseMoveToParams(string json, byte* ptr, Fdp.Modules.Geographic.IGeographicTransform geoTransform)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                Unsafe.Write(ptr, default(MoveToLocationParams));
                return;
            }

            var dto = JsonSerializer.Deserialize<MoveToLocationParamsJsonDto>(json, JsonOptions);
            if (dto == null)
            {
                Unsafe.Write(ptr, default(MoveToLocationParams));
                return;
            }

            var p = new MoveToLocationParams
            {
                Speed = dto.Speed > 0f ? dto.Speed : DefaultMoveToSpeed,
                ArrivalRadius = dto.ArrivalRadius > 0f ? dto.ArrivalRadius : 5f,
                X = dto.X,
                Y = dto.Y
            };

            // If geo-coords provided, map them
            if ((dto.TargetLat != 0 || dto.TargetLon != 0) && geoTransform != null)
            {
                var cartesian = geoTransform.ToCartesian(dto.TargetLat, dto.TargetLon, 0.0);
                p.X = cartesian.X;
                p.Y = cartesian.Y;
            }

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

        // ── WanderMilitary ─────────────────────────────────────────────────────────

        /// <summary>
        /// Maximum distance (metres) from the origin (0, 0) when picking a random
        /// wander destination.  Matches the user spec of "max distance around 1000 units".
        /// </summary>
        private const float WanderRadius = 1000f;

        /// <summary>Default travel speed for the wander doctrine (m/s).</summary>
        private const float WanderSpeed = 10f;

        /// <summary>Arrival radius for each wander waypoint (metres).</summary>
        private const float WanderArrivalRadius = 20f;

        private const string WanderMilitaryJson = """
            {
              "TreeName": "WanderMilitary",
              "Root": {
                "Type": "Action",
                "Action": "Action_Wander"
              }
            }
            """;

        /// <summary>
        /// BTree action node for the <see cref="SimHostDoctrineIds.WanderMilitary_BT"/> doctrine.
        ///
        /// <para>
        /// Each frame this action checks whether the entity has reached its current
        /// MoveTo destination (or has no active movement).  When that is the case, a new
        /// random destination within <see cref="WanderRadius"/> metres of (0, 0) is chosen
        /// and written to the <see cref="LocomotionChannel"/> as a fresh MoveTo command.
        /// </para>
        ///
        /// <para><b>Return value:</b> always <see cref="NodeStatus.Running"/> so the
        /// BTree root keeps ticking every frame indefinitely.</para>
        /// </summary>
        public static unsafe NodeStatus Action_Wander(
            ref BrainBlackboard blackboard,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int paramIndex)
        {
            if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
                return NodeStatus.Failure;

            ref var channel = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);

            // Determine if we need a new destination:
            //   • No active MoveTo action yet
            //   • Executor reported Success (arrived)
            //   • Executor reported Failure (e.g. stuck / frustration guard)
            bool needsNewTarget =
                channel.ActiveAction != NavigationConstants.ActionIdMoveTo
                || channel.Status == NodeStatus.Success
                || channel.Status == NodeStatus.Failure;

            // Also honour NavState.HasArrived as a secondary arrival signal.
            if (!needsNewTarget && ctx.World.HasComponent<NavState>(ctx.Self))
            {
                var nav = ctx.World.GetComponent<NavState>(ctx.Self);
                if (nav.HasArrived != 0)
                    needsNewTarget = true;
            }

            if (needsNewTarget)
            {
                // Pick a random destination in the square [-WanderRadius, +WanderRadius]^2
                // centred on the world origin.
                float x = (Random.Shared.NextSingle() * 2f - 1f) * WanderRadius;
                float y = (Random.Shared.NextSingle() * 2f - 1f) * WanderRadius;

                // Propagate doctrine instance id so ChannelArbitrationSystem does not
                // clear the channel on the same frame we pick a new target.
                if (ctx.World.HasComponent<DoctrineState>(ctx.Self))
                {
                    var doctrine = ctx.World.GetComponent<DoctrineState>(ctx.Self);
                    channel.DoctrineInstanceId = doctrine.InstanceId;
                }

                // Incrementing ActionInstanceId signals LocomotionDispatcherSystem to
                // call OnEnter again (re-activates MoveTo with the fresh destination).
                unchecked { channel.ActionInstanceId++; }

                channel.ActiveAction = NavigationConstants.ActionIdMoveTo;
                channel.Status       = NodeStatus.Running;

                var moveTo = new MoveToParams
                {
                    Destination   = new Vector2(x, y),
                    ArrivalRadius = WanderArrivalRadius,
                    Speed         = WanderSpeed,
                };
                fixed (byte* dst = channel.Params)
                    Unsafe.Write(dst, moveTo);
            }

            return NodeStatus.Running;
        }

        /// <summary>
        /// Builds and returns a ready-to-use BTree interpreter for the
        /// <see cref="SimHostDoctrineIds.WanderMilitary_BT"/> doctrine.
        ///
        /// The tree consists of a single <c>Action_Wander</c> action node that
        /// continuously picks random MoveTo destinations around the world origin.
        /// </summary>
        public static Interpreter<BrainBlackboard, BTreeContext> BuildWanderMilitaryInterpreter()
        {
            var registry = new ActionRegistry<BrainBlackboard, BTreeContext>();
            registry.Register("Action_Wander", Action_Wander);
            var blob = TreeCompiler.CompileFromJson(WanderMilitaryJson);
            return new Interpreter<BrainBlackboard, BTreeContext>(blob, registry);
        }

        // ── Doctrine-specific interpreter builders ─────────────────────────────

        private const string MoveToLocationJson = """
            {
              "TreeName": "MoveToLocation",
              "Root": {
                "Type": "Action",
                "Action": "Action_WriteMoveToChannel"
              }
            }
            """;

        /// <summary>
        /// Builds and returns a ready-to-use BTree interpreter for the
        /// <see cref="SimHostDoctrineIds.MoveTo_BT"/> doctrine.
        /// The tree consists of a single <c>Action_WriteMoveToChannel</c> action
        /// that writes the parsed destination into the <see cref="LocomotionChannel"/>
        /// every tick while the task is active.
        /// </summary>
        public static Interpreter<BrainBlackboard, BTreeContext> BuildMoveToLocationInterpreter()
        {
            var registry = new ActionRegistry<BrainBlackboard, BTreeContext>();
            registry.Register("Action_WriteMoveToChannel", Action_WriteMoveToChannel);
            var blob = TreeCompiler.CompileFromJson(MoveToLocationJson);
            return new Interpreter<BrainBlackboard, BTreeContext>(blob, registry);
        }

        private const string FollowRouteJson = """
            {
              "TreeName": "FollowRoute",
              "Root": {
                "Type": "Action",
                "Action": "Action_WriteFollowRouteChannel"
              }
            }
            """;

        /// <summary>
        /// Builds and returns a ready-to-use BTree interpreter for the
        /// <see cref="SimHostDoctrineIds.FollowRoute_BT"/> doctrine.
        /// </summary>
        public static Interpreter<BrainBlackboard, BTreeContext> BuildFollowRouteInterpreter()
        {
            var registry = new ActionRegistry<BrainBlackboard, BTreeContext>();
            registry.Register("Action_WriteFollowRouteChannel", Action_WriteFollowRouteChannel);
            var blob = TreeCompiler.CompileFromJson(FollowRouteJson);
            return new Interpreter<BrainBlackboard, BTreeContext>(blob, registry);
        }

        private const string JoinFormationJson = """
            {
              "TreeName": "JoinFormation",
              "Root": {
                "Type": "Action",
                "Action": "Action_WriteJoinFormationChannel"
              }
            }
            """;

        /// <summary>
        /// Builds and returns a ready-to-use BTree interpreter for the
        /// <see cref="SimHostDoctrineIds.JoinFormation_BT"/> doctrine.
        /// </summary>
        public static Interpreter<BrainBlackboard, BTreeContext> BuildJoinFormationInterpreter()
        {
            var registry = new ActionRegistry<BrainBlackboard, BTreeContext>();
            registry.Register("Action_WriteJoinFormationChannel", Action_WriteJoinFormationChannel);
            var blob = TreeCompiler.CompileFromJson(JoinFormationJson);
            return new Interpreter<BrainBlackboard, BTreeContext>(blob, registry);
        }
    }
}
