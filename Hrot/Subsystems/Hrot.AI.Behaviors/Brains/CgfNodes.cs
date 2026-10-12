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
using Fbt.Kernel;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// FastBTree action node delegates for CGF Brain-tier mission behaviors.
    /// Compiled into Hrot.AI.Behaviors so the FbtAssemblyHotReloader can load a fresh version without
    /// restarting the editor. ⚠ CE-447: an older header said a second copy lived in Hrot.CGF.Brains and had to
    /// be kept in sync — measured 2026-09-30, it does not exist; this is the only CgfNodes.
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
            /// <summary>⭐ CE-3130 — the actor's road use for this move (R-230); Unspecified ⇒ by the unit's class.</summary>
            public RoadUse RoadUse;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FollowRouteParams
        {
            /// <summary>
            /// ID of the registered trajectory in the <see cref="TrajectoryPoolManager"/> to follow; 0 = not resolved yet.
            /// ⭐ CE-2145 — resolved from <see cref="RouteNetworkId"/> by <see cref="ResolveFollowRoute"/> at start, and again by
            /// <see cref="Action_WriteFollowRouteChannel"/> while the route is not compiled (the action caches it here).
            /// </summary>
            public int   TrajectoryId;
            public float Speed;
            public bool  Loop;
            /// <summary>⭐ CE-2145 — the route entity's network id, as the authored contract named it.</summary>
            public long  RouteNetworkId;
        }

        /// <summary>
        /// Blackboard DTO for the JoinFormation behavior.
        /// ⭐ CE-2134 — also JoinFormation's declared params DTO: the curated registrar parses the
        /// JSON straight into it (identity, no resolver), so it IS what the behaviour reads
        /// (the empty JoinFormationParamsJsonDto that anchored the old contract is deleted).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct JoinFormationParams
        {
            // Reserved for future extension: nothing reads them yet, and a caller sending {} leaves both zero.
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

        // ⭐ CE-447 (2026-09-30): no private parse DTOs — the resolvers deserialise into the ONE authored contract,
        //   Hrot.Core's [Behavior(Params = …)] classes (the paramSchema a scenario is authored against). ⛔ The private
        //   copies here had drifted (float vs double, R-132: one producer per slot).

        /// <summary>
        /// Fallback travel speed (m/s) applied when a <c>MoveToLocation</c> params JSON
        /// does not carry an explicit <c>speed</c> field (e.g. legacy plans committed
        /// before the field was added).  Prevents a zero-speed command that would cause
        /// the entity to stand still indefinitely.
        /// </summary>
        private const float DefaultMoveToSpeed = 15f;

        // -- Parse methods (cold path, unsafe byte* accepted from engine delegate) --

        /// <summary>
        /// ⭐⭐ <c>CE-2023</c> ③ ("S8n") — the TYPED resolver: the authored contract (<see cref="Hrot.Map.Definitions.Behavior.MoveToLocationParamsJsonDto"/>,
        /// a struct now) in, the blackboard block out. ⭐ The generator registers BOTH arms from it — the JSON arm (a root
        /// assign, a scenario) and the from-bytes arm (a host's Behaviour Task binds the contract) — so a blueprint can start
        /// <c>MoveToLocation</c> with parameters. Geo coordinates win over Cartesian X/Y; no geo transform on the world ⇒
        /// the Cartesian pair. ⚠ An empty payload now means "not given" ⇒ the defaults (speed, 5 m), as <c>{}</c> always did.
        /// </summary>
        [Fdp.Toolkit.Behavior.BehaviorResolver("MoveToLocation")]
        public static void ResolveMoveTo(in Hrot.Map.Definitions.Behavior.MoveToLocationParamsJsonDto authored,
            ref MoveToLocationParams block, EntityRepository world, Entity self)
        {
            var geo = world.HasSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                ? world.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                : null;
            block = ConvertMoveTo(authored, geo);
        }

        /// <summary>The MoveTo conversion, given the node's geographic transform (null ⇒ Cartesian only).</summary>
        public static MoveToLocationParams ConvertMoveTo(
            in Hrot.Map.Definitions.Behavior.MoveToLocationParamsJsonDto authored,
            Fdp.Modules.Geographic.IGeographicTransform? geoTransform)
        {
            var p = new MoveToLocationParams
            {
                Speed = authored.Speed > 0 ? (float)authored.Speed : DefaultMoveToSpeed,
                ArrivalRadius = authored.ArrivalRadius > 0 ? (float)authored.ArrivalRadius : 5f,
                X = authored.X,
                Y = authored.Y,
                RoadUse = authored.RoadUse,   // CE-3130
            };

            // If geo-coords provided, map them
            if ((authored.TargetLat != 0 || authored.TargetLon != 0) && geoTransform != null)
            {
                var cartesian = geoTransform.ToCartesian(authored.TargetLat, authored.TargetLon, 0.0);
                p.X = cartesian.X;
                p.Y = cartesian.Y;
            }
            return p;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2023</c> ③ ("S8n") — the TYPED resolver for <c>FireAtTarget</c>: the authored contract's network id is
        /// mapped to the local entity through the world's <c>NetworkEntityMap</c>; both arms (JSON, host bytes) come from it.
        /// </summary>
        [Fdp.Toolkit.Behavior.BehaviorResolver("FireAtTarget")]
        public static void ResolveFireAtTarget(in Hrot.Map.Definitions.Behavior.FireAtTargetParamsJsonDto authored,
            ref FireAtTargetParams block, EntityRepository world, Entity self)
        {
            var map = (world.HasSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()
                ? world.GetSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()
                : null) ?? new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
            block = ConvertFireAtTarget(authored, map);
        }

        /// <summary>The FireAtTarget conversion, given the node's entity map.</summary>
        public static FireAtTargetParams ConvertFireAtTarget(
            in Hrot.Map.Definitions.Behavior.FireAtTargetParamsJsonDto authored,
            Fdp.Toolkit.Replication.Services.NetworkEntityMap entityMap)
        {
            long targetPacked = 0;
            if (!authored.TargetNetworkId.IsNone
                && entityMap.TryGetEntity(authored.TargetNetworkId.NetworkId, out var entity))
            {
                targetPacked = (long)entity.PackedValue;
            }
            else if (!authored.TargetNetworkId.IsNone)
            {
                BehaviorLog.ParseWarn("FireAtTarget TargetNetworkId=" + authored.TargetNetworkId.NetworkId + " not found in entity map; target will not fire.");
            }

            return new FireAtTargetParams
            {
                TargetPacked    = targetPacked,
                MaxRounds       = authored.MaxRounds,
                CooldownSeconds = authored.CooldownSeconds,
                RoundsFired     = 0,
            };
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2145</c> (docs/DESIGN_Orders_Params_Entity_Classes.md §10, R-273) — the TYPED resolver for <c>FollowRoute</c>:
        /// the RECEIVER converts the authored route entity to its compiled trajectory, so any sender (mission, HTTP, order, SOP,
        /// scenario) may send the published <c>{routeEntityId, Speed, Loop}</c>. A route not compiled yet leaves
        /// <c>TrajectoryId = 0</c>; the action looks again each tick (it replaces the mission system's assignment retry).
        /// </summary>
        [Fdp.Toolkit.Behavior.BehaviorResolver("FollowRoute")]
        public static void ResolveFollowRoute(in Hrot.Map.Definitions.Behavior.FollowRouteParamsJsonDto authored,
            ref FollowRouteParams block, EntityRepository world, Entity self)
        {
            // ⚠ an empty params string reaches a typed resolver as default — null for this class DTO (BehaviorParams.FromBlockResolver).
            long route = authored is null || authored.RouteEntityId.IsNone ? 0 : authored.RouteEntityId.NetworkId;
            block = new FollowRouteParams
            {
                RouteNetworkId = route,
                Speed          = authored?.Speed ?? 0f,
                Loop           = authored?.Loop ?? false,
                TrajectoryId   = TryFindRouteTrajectory(world, route, out int trajectory) ? trajectory : 0,
            };
        }

        /// <summary>
        /// ⭐ <c>CE-2145</c> — THE lookup from a route entity's network id to its compiled trajectory (moved from the deleted
        /// sender-side <c>MissionControlBehaviorParamsHelper</c>): the entity through the one network-id resolver
        /// (<see cref="Fdp.Toolkit.Replication.Services.NetworkIdResolver"/>, BP-508), then its <c>RouteTrajectoryCache</c>.
        /// False while the route is unknown or not compiled (<c>TrajectoryId == 0</c>).
        /// </summary>
        public static bool TryFindRouteTrajectory(EntityRepository? world, long routeNetworkId, out int trajectoryId)
        {
            trajectoryId = 0;
            if (world is null || routeNetworkId <= 0 || !world.IsComponentTypeRegistered<Hrot.Map.Common.Components.RouteTrajectoryCache>())
                return false;
            var route = Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(world, routeNetworkId);
            if (route == Entity.Null || !world.HasComponent<Hrot.Map.Common.Components.RouteTrajectoryCache>(route)) return false;
            trajectoryId = world.GetComponentRO<Hrot.Map.Common.Components.RouteTrajectoryCache>(route).TrajectoryId;
            return trajectoryId != 0;
        }

        // -- Action / Condition delegates --
        // ⭐ CE-504 C-2 — the shared C# node signature (ref P, Entity self, EntityRepository world), the same one the HSM
        //   binds. A JSON asset calls each per binding at its baked host offset (BTreeBridgeEmitCore); a curated tree binds
        //   it through SharedNodeBuilderExtensions. 📄 docs/blueprints/DESIGN_BTree_Node_Call_Shapes.md.

        /// <summary>
        /// BTree action node for the MoveToLocation behavior.
        /// Writes the parsed destination into the <see cref="LocomotionChannel"/> every tick.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Action_WriteMoveToChannel(ref MoveToLocationParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<LocomotionChannel>(self))
            {
                BehaviorLog.Error(self, world, "Entity is missing LocomotionChannel; blueprint may be misconfigured.");
                return NodeStatus.Failure;
            }

            ref var channel = ref world.GetComponentRW<LocomotionChannel>(self);
            if (world.HasComponent<BehaviorState>(self))
            {
                var behavior = world.GetComponent<BehaviorState>(self);
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
                Destination  = new Vector3(p.X, p.Y, 0f), // blueprint-authored 2-D destination: its height is not given —
                Flags        = NavigationConstants.FlagDestinationOnSurface,   //   ⭐ CE-1035 Q0b (R-252): the motion side puts it on the surface
                ArrivalRadius = p.ArrivalRadius,
                Speed        = p.Speed,
                RoadUse      = p.RoadUse,   // ⭐ CE-3130 — packed into Flags bits 5–7
            });

            return NodeStatus.Running;
        }

        /// <summary>BTree action node for the FollowRoute behavior.</summary>
        [SharedAiAction]
        public static NodeStatus Action_WriteFollowRouteChannel(ref FollowRouteParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<LocomotionChannel>(self))
            {
                BehaviorLog.Error(self, world, "Entity is missing LocomotionChannel; blueprint may be misconfigured.");
                return NodeStatus.Failure;
            }

            // ⭐ CE-2145 — the route is not compiled yet: look again, and WAIT (Running) without touching the channel.
            if (p.TrajectoryId == 0)
            {
                if (!TryFindRouteTrajectory(world, p.RouteNetworkId, out int trajectory)) return NodeStatus.Running;
                p.TrajectoryId = trajectory;
            }

            ref var channel = ref world.GetComponentRW<LocomotionChannel>(self);
            if (world.HasComponent<BehaviorState>(self))
            {
                var behavior = world.GetComponent<BehaviorState>(self);
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
        [SharedAiAction]
        public static NodeStatus Action_WriteJoinFormationChannel(ref JoinFormationParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<LocomotionChannel>(self))
            {
                BehaviorLog.Error(self, world, "Entity is missing LocomotionChannel; blueprint may be misconfigured.");
                return NodeStatus.Failure;
            }

            ref var channel = ref world.GetComponentRW<LocomotionChannel>(self);
            if (world.HasComponent<BehaviorState>(self))
            {
                var behavior = world.GetComponent<BehaviorState>(self);
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
        [SharedAiAction]
        public static NodeStatus Action_Wander(Entity self, EntityRepository world)
        {
            if (!world.HasComponent<LocomotionChannel>(self))
                return NodeStatus.Failure;

            ref var channel = ref world.GetComponentRW<LocomotionChannel>(self);

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
                var wanderRng = SimRng.FromSim((int)self.Index, 1, world.SimulationTime);
                float x = (wanderRng.NextSingle() * 2f - 1f) * WanderRadius;
                float y = (wanderRng.NextSingle() * 2f - 1f) * WanderRadius;

                // Propagate behavior instance id so ChannelArbitrationSystem does not
                // clear the channel on the same frame we pick a new target.
                if (world.HasComponent<BehaviorState>(self))
                {
                    var behavior = world.GetComponent<BehaviorState>(self);
                    channel.BehaviorInstanceId = behavior.InstanceId;
                }

                // Incrementing ActionInstanceId signals LocomotionDispatcherSystem to
                // call OnEnter again (re-activates MoveTo with the fresh destination).
                unchecked { channel.ActionInstanceId++; }

                channel.ActiveAction = NavigationConstants.ActionIdMoveTo;
                channel.Status       = NodeStatus.Running;

                WriteToLocomotionParams(ref channel, new MoveToParams
                {
                    Destination   = new Vector3(x, y, 0f), // wander target, 2-D: its height is not given —
                    Flags         = NavigationConstants.FlagDestinationOnSurface,   //   ⭐ CE-1035 Q0b (R-252): the motion side puts it on the surface
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
                if (mem.EntityIds[i] == targetPacked && mem.Freshness[i] > 0f)
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
        [SharedAiCondition]
        public static NodeStatus Condition_TargetAliveAndVisible(ref FireAtTargetParams p, Entity self, EntityRepository world)
        {
            var target = new Fdp.Core.Entity((ulong)p.TargetPacked);

            // 1. If the target is definitively dead, fail the node so the behavior finishes cleanly.
            if (!world.IsAlive(target))
                return NodeStatus.Failure;

            // 2. Wait for the perception pipeline to initialize/catch up.
            if (!world.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(self))
                return NodeStatus.Running; // FIX: Was Failure

            ref readonly var mem = ref world.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(self);

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
        [SharedAiAction]
        public static NodeStatus Action_FireAtTarget(ref FireAtTargetParams p, Entity self, EntityRepository world)
        {
            var target = new Fdp.Core.Entity((ulong)p.TargetPacked);

            // Target destroyed = mission accomplished.
            if (!world.IsAlive(target))
                return NodeStatus.Success;

            // Target out of sensor range = mission aborted.
            if (world.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(self))
            {
                ref readonly var mem = ref world.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(self);
                if (!IsTargetVisible(in mem, p.TargetPacked)) return NodeStatus.Failure;
            }

            // Max rounds reached = cease fire.
            if (p.MaxRounds > 0 && p.RoundsFired >= p.MaxRounds)
                return NodeStatus.Success;

            if (!world.HasComponent<Fdp.Toolkit.Behavior.Components.WeaponChannel>(self))
                return NodeStatus.Failure;

            ref var channel = ref world.GetComponentRW<Fdp.Toolkit.Behavior.Components.WeaponChannel>(self);

            // Sync BehaviorInstanceId so ChannelArbitrationSystem does not clear the channel every frame
            if (world.HasComponent<BehaviorState>(self))
            {
                var behavior = world.GetComponent<BehaviorState>(self);
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

            // ⭐ CE-3136 B7 (backend, cross-lane — said in the commit) — the EXECUTOR counts the rounds and ends the action after
            //   MaxRounds (AimAndFireParams.Rounds); the node no longer predicts a round from "cooldown is 0". ⛔ That prediction
            //   counted every tick of the aim time (P-3, the weapon is cooled but not yet aimed) and reached MaxRounds before a
            //   single round left — measured on HeardShotScenarioTests: the hidden shooter "fired" 30 rounds and spent none.
            int alreadyFired = p.RoundsFired;
            if (!needsActivation && channel.DispatchedInstanceId == channel.ActionInstanceId)
                p.RoundsFired = Fdp.Toolkit.Combat.Executors.AimAndFireExecutor.RoundsFiredOf(ref channel);   // the real count
            if (needsActivation)
            {
                // ⭐ CE-3158 L5 (R-256) — through the ONE aimed step (PostureNodes.Fire at a named target): the executor picks the
                //   weapon per shot (MountAuto) as every other aimed fire does. ⛔ SUPERSEDED: its own AimAndFireParams write (mount 0).
                var issue = new EngageState();
                PostureNodes.Fire(world, self, ref issue, p.CooldownSeconds,
                    p.MaxRounds > 0 ? System.Math.Max(p.MaxRounds - alreadyFired, 1) : 0, target);
            }

            return NodeStatus.Running;
        }

        /// <summary>
        /// BTree action node: holds the entity in place while waiting for a target to become
        /// visible. Always returns <see cref="NodeStatus.Running"/> so the Selector stays alive.
        /// </summary>
        public static NodeStatus Action_HoldPosition(
            ref byte blackboard,   // P4-②: the root params SLOT BASE, not a component
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
        [Behavior(BehaviorNames.MoveToLocation, Params = typeof(global::Hrot.Map.Definitions.Behavior.MoveToLocationParamsJsonDto), Categories = BehaviorCategory.AllMilitary | BehaviorCategory.Civilian)]   // ⭐ CE-2134
        [BTreeDefinition("MoveToLocation", Curated = true, Group = "Library/Basic", ParamsType = typeof(MoveToLocationParams))]
        public static BTreeBuilder<MoveToBlackboard, BTreeContext> BuildMoveToLocationTree()
        {
            return new BTreeBuilder<MoveToBlackboard, BTreeContext>()
                .Action(bb => bb.Params, Action_WriteMoveToChannel);
        }

        /// <summary>
        /// Exposes the FollowRoute BTree structure for Fbt.SourceGen static analysis.
        /// </summary>
        [Behavior(BehaviorNames.FollowRoute, Params = typeof(global::Hrot.Map.Definitions.Behavior.FollowRouteParamsJsonDto), Categories = BehaviorCategory.AllMilitary)]   // ⭐ CE-2134
        [BTreeDefinition("FollowRoute", Curated = true, Group = "Library/Basic", ParamsType = typeof(FollowRouteParams))]
        public static BTreeBuilder<FollowRouteBlackboard, BTreeContext> BuildFollowRouteTree()
        {
            return new BTreeBuilder<FollowRouteBlackboard, BTreeContext>()
                .Action(bb => bb.Params, Action_WriteFollowRouteChannel);
        }

        /// <summary>
        /// Exposes the JoinFormation BTree structure for Fbt.SourceGen static analysis.
        /// </summary>
        [Behavior(BehaviorNames.JoinFormation, Params = typeof(global::Hrot.AI.Behaviors.Brains.CgfNodes.JoinFormationParams), Categories = BehaviorCategory.Infantry)]   // ⭐ CE-2134 — what the identity parse fills (BEH003)
        [BTreeDefinition("JoinFormation", Curated = true, Group = "Library/Basic", ParamsType = typeof(JoinFormationParams))]
        public static BTreeBuilder<JoinFormationBlackboard, BTreeContext> BuildJoinFormationTree()
        {
            return new BTreeBuilder<JoinFormationBlackboard, BTreeContext>()
                .Action(bb => bb.Params, Action_WriteJoinFormationChannel);
        }

        /// <summary>
        /// Exposes the WanderMilitary BTree structure for Fbt.SourceGen static analysis.
        /// </summary>
        [Behavior(BehaviorNames.WanderMilitary, Params = typeof(global::Hrot.Map.Definitions.Behavior.NoParams), Categories = BehaviorCategory.MilitaryApc)]   // ⭐ CE-2134
        [BTreeDefinition("WanderMilitary", Curated = true, Group = "Library/Basic")]
        // ⭐ P4-②: `byte` here, and it is the RAW-DELEGATE case so nothing is lost. A selector-form
        //   builder still needs a struct with fields (which is why the wrapper structs stay — §30.18);
        //   this tree binds a delegate directly, and that delegate now takes `ref byte`.
        // ⚠ WanderMilitary declares NO params, so its entity has no root slot and the tick hands the
        //   interpreter a scratch byte. That is safe precisely because nothing here projects.
        public static BTreeBuilder<byte, BTreeContext> BuildWanderMilitaryTree()
        {
            return new BTreeBuilder<byte, BTreeContext>()
                .Action(Action_Wander);
        }

        /// <summary>
        /// Exposes the FireAtTarget BTree structure for Fbt.SourceGen static analysis.
        /// </summary>
        [Behavior(BehaviorNames.FireAtTarget, Params = typeof(global::Hrot.Map.Definitions.Behavior.FireAtTargetParamsJsonDto), Categories = BehaviorCategory.AllMilitary)]   // ⭐ CE-2134
        [BTreeDefinition("FireAtTarget", Curated = true, Group = "Library/Basic", ParamsType = typeof(FireAtTargetParams))]
        public static BTreeBuilder<FireAtTargetBlackboard, BTreeContext> BuildFireAtTargetTree()
        {
            return new BTreeBuilder<FireAtTargetBlackboard, BTreeContext>()
                .Sequence(s => s
                    .Condition(bb => bb.Params, Condition_TargetAliveAndVisible)
                    .Action(bb => bb.Params, Action_FireAtTarget));
        }
    }
}
