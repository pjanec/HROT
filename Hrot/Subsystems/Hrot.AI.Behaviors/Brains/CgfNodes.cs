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
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FollowRouteParams
        {
            /// <summary>
            /// ID of the registered trajectory in the <see cref="TrajectoryPoolManager"/> to follow.
            /// Written into <c>the root params slot</c> at spawn time and read by
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

        // ⭐ CE-447 (2026-09-30): no private parse DTOs — the resolvers deserialise into the ONE authored contract,
        //   Hrot.Core's [BehaviorContract] classes (the paramSchema a scenario is authored against). ⛔ The private
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
                Y = authored.Y
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
            if (authored.TargetNetworkId != 0
                && entityMap.TryGetEntity(authored.TargetNetworkId, out var entity))
            {
                targetPacked = (long)entity.PackedValue;
            }
            else if (authored.TargetNetworkId != 0)
            {
                BehaviorLog.ParseWarn("FireAtTarget TargetNetworkId=" + authored.TargetNetworkId + " not found in entity map; target will not fire.");
            }

            return new FireAtTargetParams
            {
                TargetPacked    = targetPacked,
                MaxRounds       = authored.MaxRounds,
                CooldownSeconds = authored.CooldownSeconds,
                RoundsFired     = 0,
            };
        }

        // 3-param shape: the generator emits the (json, memory, capacity, world, self, host)
        // adapter the hand-written registrar used to spell out by hand.
        [Fdp.Toolkit.Behavior.BehaviorResolver("FollowRoute")]
        public static unsafe void ParseFollowRouteParams(string json, byte* ptr, int capacity)
        {
            var p = string.IsNullOrWhiteSpace(json)
                ? default
                : JsonSerializer.Deserialize<FollowRouteParams>(json, JsonOptions);
            Unsafe.Write(ptr, p);
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
                Destination  = new Vector3(p.X, p.Y, 0f), // blueprint-authored 2D destination (§0.2)
                ArrivalRadius = p.ArrivalRadius,
                Speed        = p.Speed
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
            if (world.HasComponent<Fdp.Toolkit.Combat.Components.WeaponState>(self))
            {
                var weapon = world.GetComponent<Fdp.Toolkit.Combat.Components.WeaponState>(self);
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
        [BTreeDefinition("MoveToLocation", Curated = true, ParamsType = typeof(MoveToLocationParams))]
        public static BTreeBuilder<MoveToBlackboard, BTreeContext> BuildMoveToLocationTree()
        {
            return new BTreeBuilder<MoveToBlackboard, BTreeContext>()
                .Action(bb => bb.Params, Action_WriteMoveToChannel);
        }

        /// <summary>
        /// Exposes the FollowRoute BTree structure for Fbt.SourceGen static analysis.
        /// </summary>
        [BTreeDefinition("FollowRoute", Curated = true, ParamsType = typeof(FollowRouteParams))]
        public static BTreeBuilder<FollowRouteBlackboard, BTreeContext> BuildFollowRouteTree()
        {
            return new BTreeBuilder<FollowRouteBlackboard, BTreeContext>()
                .Action(bb => bb.Params, Action_WriteFollowRouteChannel);
        }

        /// <summary>
        /// Exposes the JoinFormation BTree structure for Fbt.SourceGen static analysis.
        /// </summary>
        [BTreeDefinition("JoinFormation", Curated = true, ParamsType = typeof(JoinFormationParams))]
        public static BTreeBuilder<JoinFormationBlackboard, BTreeContext> BuildJoinFormationTree()
        {
            return new BTreeBuilder<JoinFormationBlackboard, BTreeContext>()
                .Action(bb => bb.Params, Action_WriteJoinFormationChannel);
        }

        /// <summary>
        /// Exposes the WanderMilitary BTree structure for Fbt.SourceGen static analysis.
        /// </summary>
        [BTreeDefinition("WanderMilitary", Curated = true)]
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
        [BTreeDefinition("FireAtTarget", Curated = true, ParamsType = typeof(FireAtTargetParams))]
        public static BTreeBuilder<FireAtTargetBlackboard, BTreeContext> BuildFireAtTargetTree()
        {
            return new BTreeBuilder<FireAtTargetBlackboard, BTreeContext>()
                .Sequence(s => s
                    .Condition(bb => bb.Params, Condition_TargetAliveAndVisible)
                    .Action(bb => bb.Params, Action_FireAtTarget));
        }
    }
}
