using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using CarKinem.Core;
using Fdp.Core;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Serialization;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Hrot.AI.Behaviors.Logging;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// FastBTree action node delegates for CGF Brain-tier mission behaviors.
    /// Hot-reloadable copy compiled independently into Hrot.AI.Behaviors so the
    /// FbtAssemblyHotReloader can load a fresh version without restarting the editor.
    /// Source of truth is Hrot.CGF.Brains.CgfNodes; keep both files in sync.
    /// </summary>
    public static class CgfNodes
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true
        };

        // -- Channel write helpers --

        private static unsafe void WriteToLocomotionParams<T>(ref LocomotionChannel channel, T value)
            where T : unmanaged
        {
            Unsafe.As<byte, T>(ref channel.Params[0]) = value;
        }

        private static unsafe void WriteToWeaponParams<T>(ref WeaponChannel channel, T value)
            where T : unmanaged
        {
            Unsafe.As<byte, T>(ref channel.Params[0]) = value;
        }

        // -- Typed blackboard wrappers --
        // These single-field structs are used as the TBlackboard type in the
        // BTreeBuilder expression-binding overloads.  Fbt.SourceGen calculates
        // the byte offset of the Params field at compile time and emits a
        // zero-pointer bridge closure into FbtActionRegistrar.g.cs that projects
        // the runtime BrainBlackboard to the exact DTO using Unsafe.As.

        /// <summary>Typed blackboard wrapper for the MoveToLocation behavior.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MoveToBlackboard { public MoveToLocationParams Params; }

        /// <summary>Typed blackboard wrapper for the FollowRoute behavior.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FollowRouteBlackboard { public FollowRouteParams Params; }

        /// <summary>Typed blackboard wrapper for the JoinFormation behavior.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct JoinFormationBlackboard { public JoinFormationParams Params; }

        /// <summary>Typed blackboard wrapper for the FireAtTarget behavior.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FireAtTargetBlackboard { public FireAtTargetParams Params; }

        // -- Param DTO structs --

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
            /// Written into <see cref="BrainBlackboard.BehaviorParameters"/> at spawn time and read by
            /// <see cref="Action_WriteFollowRouteChannel"/> to populate the locomotion channel.
            /// </summary>
            public int   TrajectoryId;
            public float Speed;
            public bool  Loop;
        }

        /// <summary>
        /// Blackboard DTO for the JoinFormation behavior.
        /// Currently parameterless (the contract exists to satisfy the
        /// ReusableActionDelegate signature pattern). Populated to defaults
        /// by JoinFormationParamsJsonDto which carries no JSON fields.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct JoinFormationParams
        {
            // Reserved for future extension; both fields are zero-initialised
            // because JoinFormationParamsJsonDto is currently parameterless.
            public int  LeaderNetworkId;
            public byte FormationTypeId;
        }

        /// <summary>
        /// Blackboard layout for the FireAtTarget behavior (20 bytes total):
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

        // -- JSON parse DTOs (private) --

        private class MoveToLocationParamsJsonDto
        {
            public double TargetLat { get; set; }
            public double TargetLon { get; set; }
            public float Speed { get; set; }
            public float ArrivalRadius { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
        }

        private class FireAtTargetParamsJsonDto
        {
            public long  TargetNetworkId  { get; set; }
            public int   MaxRounds        { get; set; }
            public float CooldownSeconds  { get; set; }
        }

        /// <summary>
        /// Fallback travel speed (m/s) applied when a <c>MoveToLocation</c> params JSON
        /// does not carry an explicit <c>speed</c> field (e.g. legacy plans committed
        /// before the field was added).  Prevents a zero-speed command that would cause
        /// the entity to stand still indefinitely.
        /// </summary>
        private const float DefaultMoveToSpeed = 15f;

        // -- Parse methods (cold path, unsafe byte* accepted from engine delegate) --

        /// <summary>
        /// Resolver (ParseParamsDelegate shape): fetches the geographic transform from the world
        /// singleton and delegates to <see cref="ParseMoveToParams"/>. Null geo → Cartesian fallback.
        /// </summary>
        public static unsafe void ResolveMoveToParams(string json, byte* ptr, EntityRepository world, Entity self, IHostVariableAccess? host)
        {
            var geo = world.HasSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                ? world.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                : null;
            ParseMoveToParams(json, ptr, geo!);
        }

        /// <summary>
        /// Resolver (ParseParamsDelegate shape): fetches the NetworkEntityMap from the world
        /// singleton and delegates to <see cref="ParseFireAtTargetParams"/>.
        /// </summary>
        public static unsafe void ResolveFireAtTargetParams(string json, byte* ptr, EntityRepository world, Entity self, IHostVariableAccess? host)
        {
            var map = (world.HasSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()
                ? world.GetSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()
                : null) ?? new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
            ParseFireAtTargetParams(json, ptr, map);
        }

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
                BehaviorLog.ParseWarn("FireAtTarget JSON deserialized to null; using default params.");
                Unsafe.Write(ptr, default(FireAtTargetParams));
                return;
            }

            long targetPacked = 0;
            if (dto.TargetNetworkId != 0
                && entityMap.TryGetEntity(dto.TargetNetworkId, out var entity))
            {
                targetPacked = (long)entity.PackedValue;
            }
            else if (dto.TargetNetworkId != 0)
            {
                BehaviorLog.ParseWarn("FireAtTarget TargetNetworkId=" + dto.TargetNetworkId + " not found in entity map; target will not fire.");
            }

            Unsafe.Write(ptr, new FireAtTargetParams
            {
                TargetPacked    = targetPacked,
                MaxRounds       = dto.MaxRounds,
                CooldownSeconds = dto.CooldownSeconds,
                RoundsFired     = 0,
            });
        }

        // -- Action / Condition delegates --
        // Three-param ReusableActionDelegate<TValue, BTreeContext> signatures.
        // The [BTreeAction] / [BTreeCondition] attributes cause Fbt.SourceGen to
        // emit bridge closures in FbtActionRegistrar.g.cs that project the runtime
        // BrainBlackboard to TValue using Unsafe.As at byte offset 0.
        // No 'unsafe' keyword or 'fixed' blocks appear in any of these methods.

        /// <summary>
        /// BTree action node for the MoveToLocation behavior.
        /// Writes the parsed destination into the <see cref="LocomotionChannel"/> every tick.
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_WriteMoveToChannel(
            ref MoveToLocationParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
            {
                BehaviorLog.Error(ref ctx, "Entity is missing LocomotionChannel; blueprint may be misconfigured.");
                return NodeStatus.Failure;
            }

            ref var channel = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
            if (ctx.World.HasComponent<BehaviorState>(ctx.Self))
            {
                var behavior = ctx.World.GetComponent<BehaviorState>(ctx.Self);
                channel.BehaviorInstanceId = behavior.InstanceId;
            }

            bool needsActivation = channel.ActiveAction != NavigationConstants.ActionIdMoveTo
                || channel.Status == NodeStatus.Failure;

            // If this action is already active, forward the executor's terminal status so
            // the BTree can finish and publish BehaviorFinishedEvent.
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

            WriteToLocomotionParams(ref channel, new MoveToParams
            {
                Destination  = new Vector3(p.X, p.Y, 0f), // blueprint-authored 2D destination (§0.2)
                ArrivalRadius = p.ArrivalRadius,
                Speed        = p.Speed
            });

            return NodeStatus.Running;
        }

        /// <summary>BTree action node for the FollowRoute behavior.</summary>
        [BTreeAction]
        public static NodeStatus Action_WriteFollowRouteChannel(
            ref FollowRouteParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
            {
                BehaviorLog.Error(ref ctx, "Entity is missing LocomotionChannel; blueprint may be misconfigured.");
                return NodeStatus.Failure;
            }

            ref var channel = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
            if (ctx.World.HasComponent<BehaviorState>(ctx.Self))
            {
                var behavior = ctx.World.GetComponent<BehaviorState>(ctx.Self);
                channel.BehaviorInstanceId = behavior.InstanceId;
            }

            bool needsActivation = channel.ActiveAction != NavigationConstants.ActionIdFollowRoute
                || channel.Status == NodeStatus.Failure;
            if (needsActivation)
                unchecked { channel.ActionInstanceId++; }

            channel.ActiveAction = NavigationConstants.ActionIdFollowRoute;

            WriteToLocomotionParams(ref channel, new Fdp.Toolkit.Navigation.FollowRouteParams
            {
                TrajectoryId = p.TrajectoryId, // FIX: was hardcoded to 0; now reads from blackboard params
                IsLooped     = (byte)(p.Loop ? 1 : 0)
            });

            return NodeStatus.Running;
        }

        /// <summary>BTree action node for the JoinFormation behavior.</summary>
        [BTreeAction]
        public static NodeStatus Action_WriteJoinFormationChannel(
            ref JoinFormationParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            if (!ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
            {
                BehaviorLog.Error(ref ctx, "Entity is missing LocomotionChannel; blueprint may be misconfigured.");
                return NodeStatus.Failure;
            }

            ref var channel = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);
            if (ctx.World.HasComponent<BehaviorState>(ctx.Self))
            {
                var behavior = ctx.World.GetComponent<BehaviorState>(ctx.Self);
                channel.BehaviorInstanceId = behavior.InstanceId;
            }

            bool needsActivation = channel.ActiveAction != NavigationConstants.ActionIdJoinFormation
                || channel.Status == NodeStatus.Failure;
            if (needsActivation)
                unchecked { channel.ActionInstanceId++; }

            channel.ActiveAction = NavigationConstants.ActionIdJoinFormation;
            return NodeStatus.Running;
        }

        // -- WanderMilitary --

        /// <summary>
        /// Maximum distance (metres) from the origin (0, 0) when picking a random
        /// wander destination.  Matches the user spec of "max distance around 1000 units".
        /// </summary>
        private const float WanderRadius = 1000f;

        /// <summary>Default travel speed for the wander behavior (m/s).</summary>
        private const float WanderSpeed = 10f;

        /// <summary>Arrival radius for each wander waypoint (metres).</summary>
        private const float WanderArrivalRadius = 20f;

        /// <summary>
        /// BTree action node for the WanderMilitary behavior.
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
        [BTreeAction]
        public static NodeStatus Action_Wander(
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
                // ⭐ CE-202 — one generator, TWO draws. A stateless seed-per-call would have handed
                //   x == y and sent every wanderer down the diagonal; SimRng advances per draw.
                //   The salt (1) distinguishes this call site from the firing-slot pick, which is
                //   seeded from the same entity and tick.
                var wanderRng = SimRng.FromSim((int)ctx.Self.Index, 1, ctx.World.SimulationTime);
                float x = (wanderRng.NextSingle() * 2f - 1f) * WanderRadius;
                float y = (wanderRng.NextSingle() * 2f - 1f) * WanderRadius;

                // Propagate behavior instance id so ChannelArbitrationSystem does not
                // clear the channel on the same frame we pick a new target.
                if (ctx.World.HasComponent<BehaviorState>(ctx.Self))
                {
                    var behavior = ctx.World.GetComponent<BehaviorState>(ctx.Self);
                    channel.BehaviorInstanceId = behavior.InstanceId;
                }

                // Incrementing ActionInstanceId signals LocomotionDispatcherSystem to
                // call OnEnter again (re-activates MoveTo with the fresh destination).
                unchecked { channel.ActionInstanceId++; }

                channel.ActiveAction = NavigationConstants.ActionIdMoveTo;
                channel.Status       = NodeStatus.Running;

                WriteToLocomotionParams(ref channel, new MoveToParams
                {
                    Destination   = new Vector3(x, y, 0f), // wander target, 2D-authored (§0.2)
                    ArrivalRadius = WanderArrivalRadius,
                    Speed         = WanderSpeed,
                });
            }

            return NodeStatus.Running;
        }

        // -- FireAtTarget --

        // Isolated unsafe helper so that Condition_TargetAliveAndVisible and
        // Action_FireAtTarget themselves need no unsafe keyword.
        private static unsafe bool IsTargetVisible(
            in Fdp.Toolkit.Perception.Components.TargetMemory mem,
            long targetPacked)
        {
            for (int i = 0; i < mem.Count; i++)
            {
                if (mem.EntityIds[i] == targetPacked && mem.ThreatScores[i] > 0f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// BTree condition node: returns Success when the target entity is alive and
        /// currently tracked in the entity's TargetMemory (visible + threat score &gt; 0).
        /// Return Failure when the target is dead.
        /// Return Running while the target is alive but out of sight.
        /// </summary>
        [BTreeCondition]
        public static NodeStatus Condition_TargetAliveAndVisible(
            ref FireAtTargetParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            var target = new Fdp.Core.Entity((ulong)p.TargetPacked);

            // 1. If the target is definitively dead, fail the node so the behavior finishes cleanly.
            if (!ctx.World.IsAlive(target))
                return NodeStatus.Failure;

            // 2. Wait for the perception pipeline to initialize/catch up.
            if (!ctx.World.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(ctx.Self))
                return NodeStatus.Running; // FIX: Was Failure

            ref readonly var mem = ref ctx.World.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(ctx.Self);

            // 3. Target is visible! Proceed to the next node in the Sequence.
            if (IsTargetVisible(in mem, p.TargetPacked))
                return NodeStatus.Success;

            // 4. Target is alive, but not currently visible.
            // Return Running to block the Sequence and force a re-evaluation next tick!
            return NodeStatus.Running; // FIX: Was Failure
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
        [BTreeAction]
        public static NodeStatus Action_FireAtTarget(
            ref FireAtTargetParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            var target = new Fdp.Core.Entity((ulong)p.TargetPacked);

            // Target destroyed = mission accomplished.
            if (!ctx.World.IsAlive(target))
                return NodeStatus.Success;

            // Target out of sensor range = mission aborted.
            if (ctx.World.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(ctx.Self))
            {
                ref readonly var mem = ref ctx.World.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(ctx.Self);
                if (!IsTargetVisible(in mem, p.TargetPacked)) return NodeStatus.Failure;
            }

            // Max rounds reached = cease fire.
            if (p.MaxRounds > 0 && p.RoundsFired >= p.MaxRounds)
                return NodeStatus.Success;

            if (!ctx.World.HasComponent<Fdp.Toolkit.Behavior.Components.WeaponChannel>(ctx.Self))
                return NodeStatus.Failure;

            ref var channel = ref ctx.World.GetComponentRW<Fdp.Toolkit.Behavior.Components.WeaponChannel>(ctx.Self);

            // Sync BehaviorInstanceId so ChannelArbitrationSystem does not clear the channel every frame
            if (ctx.World.HasComponent<BehaviorState>(ctx.Self))
            {
                var behavior = ctx.World.GetComponent<BehaviorState>(ctx.Self);
                channel.BehaviorInstanceId = behavior.InstanceId;
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
                WriteToWeaponParams(ref channel, new Fdp.Toolkit.Combat.Executors.AimAndFireParams
                {
                    Target          = target,
                    CooldownSeconds = p.CooldownSeconds,
                });

                unchecked { channel.ActionInstanceId++; }
                channel.ActiveAction = Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire;
            }

            // Only increment RoundsFired exactly when the weapon is ready to shoot this tick.
            // Writing back through the ref parameter updates the blackboard in-place -- no
            // pointer arithmetic required.
            if (ctx.World.HasComponent<Fdp.Toolkit.Combat.Components.WeaponState>(ctx.Self))
            {
                var weapon = ctx.World.GetComponent<Fdp.Toolkit.Combat.Components.WeaponState>(ctx.Self);
                if (weapon.CooldownSecondsRemaining <= 0f)
                    p.RoundsFired = p.RoundsFired + 1;
            }

            return NodeStatus.Running;
        }

        /// <summary>
        /// BTree action node: holds the entity in place while waiting for a target to become
        /// visible. Always returns <see cref="NodeStatus.Running"/> so the Selector stays alive.
        /// </summary>
        public static NodeStatus Action_HoldPosition(
            ref BrainBlackboard blackboard,
            ref BehaviorTreeState state,
            ref BTreeContext ctx,
            int paramIndex)
        {
            return NodeStatus.Running;
        }

        // -- [BTreeDefinition] builder methods --
        // Fbt.SourceGen scans these at compile time and emits FbtTreeCatalog.g.cs
        // with Get<Name>() methods that call .Compile("<Name>") on first access.
        // CgfBehaviorSetup retrieves the pre-compiled blobs from that catalog at startup.

        /// <summary>
        /// Exposes the MoveToLocation BTree structure for Fbt.SourceGen static analysis.
        /// </summary>
        [BTreeDefinition("MoveToLocation")]
        public static BTreeBuilder<MoveToBlackboard, BTreeContext> BuildMoveToLocationTree()
        {
            return new BTreeBuilder<MoveToBlackboard, BTreeContext>()
                .Action(bb => bb.Params, Action_WriteMoveToChannel);
        }

        /// <summary>
        /// Exposes the FollowRoute BTree structure for Fbt.SourceGen static analysis.
        /// </summary>
        [BTreeDefinition("FollowRoute")]
        public static BTreeBuilder<FollowRouteBlackboard, BTreeContext> BuildFollowRouteTree()
        {
            return new BTreeBuilder<FollowRouteBlackboard, BTreeContext>()
                .Action(bb => bb.Params, Action_WriteFollowRouteChannel);
        }

        /// <summary>
        /// Exposes the JoinFormation BTree structure for Fbt.SourceGen static analysis.
        /// </summary>
        [BTreeDefinition("JoinFormation")]
        public static BTreeBuilder<JoinFormationBlackboard, BTreeContext> BuildJoinFormationTree()
        {
            return new BTreeBuilder<JoinFormationBlackboard, BTreeContext>()
                .Action(bb => bb.Params, Action_WriteJoinFormationChannel);
        }

        /// <summary>
        /// Exposes the WanderMilitary BTree structure for Fbt.SourceGen static analysis.
        /// </summary>
        [BTreeDefinition("WanderMilitary")]
        public static BTreeBuilder<BrainBlackboard, BTreeContext> BuildWanderMilitaryTree()
        {
            return new BTreeBuilder<BrainBlackboard, BTreeContext>()
                .Action(Action_Wander);
        }

        /// <summary>
        /// Exposes the FireAtTarget BTree structure for Fbt.SourceGen static analysis.
        /// </summary>
        [BTreeDefinition("FireAtTarget")]
        public static BTreeBuilder<FireAtTargetBlackboard, BTreeContext> BuildFireAtTargetTree()
        {
            return new BTreeBuilder<FireAtTargetBlackboard, BTreeContext>()
                .Sequence(s => s
                    .Condition(bb => bb.Params, Condition_TargetAliveAndVisible)
                    .Action(bb => bb.Params, Action_FireAtTarget));
        }
    }
}
