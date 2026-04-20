using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using CarKinem.Core;
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;

namespace Hrot.CGF.Brains
{
    /// <summary>
    /// FastBTree action node delegates for CGF Brain-tier mission doctrines.
    /// Relocated from Hrot.SimHost.Brains to Hrot.CGF.Brains as part of the
    /// Brain/Muscle architectural split (modular-2 feedback-1).
    /// </summary>
    public static class CgfNodes
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
            /// <summary>
            /// ID of the registered trajectory in the <see cref="TrajectoryPoolManager"/> to follow.
            /// Written into <see cref="BrainBlackboard.Memory"/> at spawn time and read by
            /// <see cref="Action_WriteFollowRouteChannel"/> to populate the locomotion channel.
            /// </summary>
            public int   TrajectoryId;
            public float Speed;
            public bool  Loop;
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

            // If this action is already active, forward the executor's terminal status so
            // the BTree can finish and publish DoctrineFinishedEvent.
            if (!needsActivation)
            {
                if (channel.Status == NodeStatus.Success)
                    return NodeStatus.Success;
                if (channel.Status == NodeStatus.Failure)
                    return NodeStatus.Failure;
            }

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

            var route = new Fdp.Toolkit.Navigation.FollowRouteParams
            {
                TrajectoryId = p.TrajectoryId, // FIX: was hardcoded to 0; now reads from blackboard params
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
        /// BTree action node for the WanderMilitary doctrine.
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
            //   * No active MoveTo action yet
            //   * Executor reported Success (arrived)
            //   * Executor reported Failure (e.g. stuck / frustration guard)
            bool needsNewTarget =
                channel.ActiveAction != NavigationConstants.ActionIdMoveTo
                || channel.Status == NodeStatus.Success
                || channel.Status == NodeStatus.Failure;

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
        /// Builds and returns a ready-to-use BTree interpreter for the WanderMilitary doctrine.
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
        /// Builds and returns a ready-to-use BTree interpreter for the MoveTo_BT doctrine.
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
        /// Builds and returns a ready-to-use BTree interpreter for the FollowRoute_BT doctrine.
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
        /// Builds and returns a ready-to-use BTree interpreter for the JoinFormation_BT doctrine.
        /// </summary>
        public static Interpreter<BrainBlackboard, BTreeContext> BuildJoinFormationInterpreter()
        {
            var registry = new ActionRegistry<BrainBlackboard, BTreeContext>();
            registry.Register("Action_WriteJoinFormationChannel", Action_WriteJoinFormationChannel);
            var blob = TreeCompiler.CompileFromJson(JoinFormationJson);
            return new Interpreter<BrainBlackboard, BTreeContext>(blob, registry);
        }

        // ── FireAtTarget ───────────────────────────────────────────────────────

        /// <summary>
        /// Blackboard layout for the FireAtTarget doctrine (20 bytes total):
        ///   [0..7]   TargetPacked (long)  - Entity.PackedValue of the target
        ///   [8..11]  MaxRounds    (int)   - 0 = unlimited
        ///   [12..15] CooldownSeconds (float) - seconds between shots
        ///   [16..19] RoundsFired  (int)   - runtime state, initialized to 0
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FireAtTargetParams
        {
            /// <summary>Packed ECS entity value of the target. 0 = no target resolved yet.</summary>
            public long  TargetPacked;
            /// <summary>Maximum number of fire activations. 0 = unlimited.</summary>
            public int   MaxRounds;
            /// <summary>Seconds to wait between successive shots.</summary>
            public float CooldownSeconds;
            /// <summary>Runtime counter of fire activations (written back to blackboard).</summary>
            public int   RoundsFired;
        }

        private class FireAtTargetParamsJsonDto
        {
            public long  TargetNetworkId  { get; set; }
            public int   MaxRounds        { get; set; }
            public float CooldownSeconds  { get; set; }
        }

        /// <summary>
        /// Parses FireAtTarget JSON params and writes the resolved
        /// <see cref="FireAtTargetParams"/> into the blackboard memory pointer.
        /// </summary>
        public static unsafe void ParseFireAtTargetParams(
            string json, byte* ptr,
            Fdp.Toolkit.Replication.Services.NetworkEntityMap entityMap)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                Unsafe.Write(ptr, default(FireAtTargetParams));
                return;
            }

            var dto = JsonSerializer.Deserialize<FireAtTargetParamsJsonDto>(json, JsonOptions);
            if (dto == null)
            {
                Unsafe.Write(ptr, default(FireAtTargetParams));
                return;
            }

            long targetPacked = 0;
            if (dto.TargetNetworkId != 0
                && entityMap.TryGetEntity(dto.TargetNetworkId, out var entity))
            {
                targetPacked = (long)entity.PackedValue;
            }

            Unsafe.Write(ptr, new FireAtTargetParams
            {
                TargetPacked    = targetPacked,
                MaxRounds       = dto.MaxRounds,
                CooldownSeconds = dto.CooldownSeconds,
                RoundsFired     = 0,
            });
        }

        /// <summary>
        /// BTree condition node: returns Success when the target entity is alive and
        /// currently tracked in the entity's TargetMemory (visible + threat score > 0).
        /// Returns Failure when the target is dead or out of sensor range.
        /// </summary>
        public static unsafe NodeStatus Condition_TargetAliveAndVisible(
            ref BrainBlackboard blackboard,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int paramIndex)
        {
            FireAtTargetParams p;
            fixed (byte* src = blackboard.Memory)
                p = Unsafe.Read<FireAtTargetParams>(src);

            var target = new Fdp.Core.Entity((ulong)p.TargetPacked);

            if (!ctx.World.IsAlive(target))
                return NodeStatus.Failure;

            if (!ctx.World.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(ctx.Self))
                return NodeStatus.Failure;

            ref readonly var mem = ref ctx.World.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(ctx.Self);
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] == p.TargetPacked && mem.ThreatScores[i] > 0f)
                    return NodeStatus.Success;
            }

            return NodeStatus.Failure;
        }

        /// <summary>
        /// BTree action node: manages continuous firing at the configured target via
        /// <see cref="Fdp.Toolkit.Behavior.Components.WeaponChannel"/> and the AimAndFire executor.
        ///
        /// <list type="bullet">
        ///   <item>Returns Success when the target is destroyed or max rounds are exhausted.</item>
        ///   <item>Returns Failure when the target leaves sensor range.</item>
        ///   <item>Returns Running while actively firing.</item>
        /// </list>
        /// </summary>
        public static unsafe NodeStatus Action_FireAtTarget(
            ref BrainBlackboard blackboard,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int paramIndex)
        {
            FireAtTargetParams p;
            fixed (byte* src = blackboard.Memory)
                p = Unsafe.Read<FireAtTargetParams>(src);

            var target = new Fdp.Core.Entity((ulong)p.TargetPacked);

            // Target destroyed = mission accomplished.
            if (!ctx.World.IsAlive(target))
                return NodeStatus.Success;

            // Target out of sensor range = mission aborted.
            if (ctx.World.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(ctx.Self))
            {
                bool visible = false;
                ref readonly var mem = ref ctx.World.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(ctx.Self);
                for (int i = 0; i < mem.Count; i++)
                {
                    if (mem.EntityIds[i] == p.TargetPacked && mem.ThreatScores[i] > 0f)
                    {
                        visible = true;
                        break;
                    }
                }
                if (!visible) return NodeStatus.Failure;
            }

            // Max rounds reached = cease fire.
            if (p.MaxRounds > 0 && p.RoundsFired >= p.MaxRounds)
                return NodeStatus.Success;

            if (!ctx.World.HasComponent<Fdp.Toolkit.Behavior.Components.WeaponChannel>(ctx.Self))
                return NodeStatus.Failure;

            ref var channel = ref ctx.World.GetComponentRW<Fdp.Toolkit.Behavior.Components.WeaponChannel>(ctx.Self);

            // Sync DoctrineInstanceId so ChannelArbitrationSystem doesn't clear the channel every frame
            if (ctx.World.HasComponent<DoctrineState>(ctx.Self))
            {
                var doctrine = ctx.World.GetComponent<DoctrineState>(ctx.Self);
                channel.DoctrineInstanceId = doctrine.InstanceId;
            }

            // Propagate executor success (target died mid-fire session).
            if (channel.Status == Fbt.NodeStatus.Success
                && channel.ActiveAction == Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire)
            {
                return NodeStatus.Success;
            }

            bool needsActivation =
                channel.ActiveAction != Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire
                || channel.Status    == Fbt.NodeStatus.Failure;

            if (needsActivation)
            {
                fixed (byte* ptr = channel.Params)
                    *(Fdp.Toolkit.Combat.Executors.AimAndFireParams*)ptr =
                        new Fdp.Toolkit.Combat.Executors.AimAndFireParams
                        {
                            Target          = target,
                            CooldownSeconds = p.CooldownSeconds,
                        };

                unchecked { channel.ActionInstanceId++; }
                channel.ActiveAction = Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire;
            }

            // Only increment RoundsFired exactly when the weapon is ready to shoot this tick
            if (ctx.World.HasComponent<Fdp.Toolkit.Combat.Components.WeaponState>(ctx.Self))
            {
                var weapon = ctx.World.GetComponent<Fdp.Toolkit.Combat.Components.WeaponState>(ctx.Self);
                if (weapon.CooldownSecondsRemaining <= 0f)
                {
                    fixed (byte* src = blackboard.Memory)
                    {
                        FireAtTargetParams* paramsPtr = (FireAtTargetParams*)src;
                        paramsPtr->RoundsFired = p.RoundsFired + 1;
                    }
                }
            }

            return NodeStatus.Running;
        }
        
        private const string FireAtTargetJson = """
            {
              "TreeName": "FireAtTarget",
              "Root": {
                "Type": "Sequence",
                "Children": [
                  { "Type": "Condition", "Action": "Condition_TargetAliveAndVisible" },
                  { "Type": "Action",    "Action": "Action_FireAtTarget" }
                ]
              }
            }
            """;

        /// <summary>
        /// Builds and returns a ready-to-use BTree interpreter for the FireAtTarget_BT doctrine.
        /// </summary>
        public static Interpreter<BrainBlackboard, BTreeContext> BuildFireAtTargetInterpreter()
        {
            var registry = new ActionRegistry<BrainBlackboard, BTreeContext>();
            registry.Register("Condition_TargetAliveAndVisible", Condition_TargetAliveAndVisible);
            registry.Register("Action_FireAtTarget",             Action_FireAtTarget);
            var blob = TreeCompiler.CompileFromJson(FireAtTargetJson);
            return new Interpreter<BrainBlackboard, BTreeContext>(blob, registry);
        }
    }
}
