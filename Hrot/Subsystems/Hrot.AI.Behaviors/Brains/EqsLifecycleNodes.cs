using System.Runtime.InteropServices;
using Fbt;
using Fbt.Runtime;
using FDP.Eqs;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// FastBTree blackboard parameters for EQS lifecycle actions.
    /// Laid out sequentially so the Blueprint generator can emit correct field offsets.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EqsParams
    {
        /// <summary>Blueprint ID of the EQS query to execute.</summary>
        public uint  BlueprintId;
        /// <summary>World-space search radius in metres.</summary>
        public float SearchRadius;
        /// <summary>Minimum threat score; results below this value are excluded.</summary>
        public float ThreatThreshold;
        /// <summary>Faction bitmask used to filter candidate entities.</summary>
        public uint  FactionFilter;
        /// <summary>Score change threshold for the ScoreDelta publish policy.</summary>
        public float ScoreDeltaThreshold;
        /// <summary>Context slot 0 (by convention: Self/Observer).</summary>
        public Entity ContextSlot0;
        /// <summary>Context slot 1 (by convention: Target). Primary LOS position source.</summary>
        public Entity ContextSlot1;
        /// <summary>Context slot 2 (by convention: Leader/Squad-mate).</summary>
        public Entity ContextSlot2;
    }

    /// <summary>
    /// Blackboard parameters for child-sensor spawn/destroy actions.
    /// Laid out sequentially so the Blueprint generator can emit correct field offsets.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EqsSpawnParams
    {
        /// <summary>EQS query parameters -- copied to the child EqsSensor.</summary>
        public EqsParams SensorConfig;
        /// <summary>Discriminates multiple child sensors on the same parent.
        /// Values 0..254 allowed; 255 is reserved.</summary>
        public byte ChildSlotIndex;
        /// <summary>Output: handle to the spawned child entity. Also serves as a
        /// persistent cache to avoid double-spawning on re-entry.</summary>
        public EqsSensorHandle SpawnedHandle;
    }

    /// <summary>
    /// FastBTree action and deactivator nodes for the EQS sensor lifecycle.
    ///
    /// <para><b>Usage pattern (in a BTree definition):</b></para>
    /// <code>
    ///   Parallel(
    ///     builder.Action&lt;EqsParams&gt;(EqsLifecycleNodes.Action_MaintainEqsSensor),
    ///     builder.Action&lt;EqsParams&gt;(EqsLifecycleNodes.Action_WaitForSensor))
    /// </code>
    ///
    /// <para><c>Action_MaintainEqsSensor</c> is a persistent action (always Running).
    /// Its deactivator removes <see cref="EqsSensor"/> and <see cref="EqsCognitiveBuffer"/>
    /// when the enclosing sub-tree is aborted (e.g. behavior change, Parallel abort).</para>
    ///
    /// <para><c>Action_WaitForSensor</c> polls <see cref="EqsCognitiveBuffer.IsReady"/>;
    /// returns Success once the first solver result has been written.</para>
    /// </summary>
    public static class EqsLifecycleNodes
    {
        // ── Action_MaintainEqsSensor ──────────────────────────────────────────

        /// <summary>
        /// Persistent action that keeps an <see cref="EqsSensor"/> component attached to
        /// the entity and synchronised with the current blackboard parameters.
        ///
        /// <list type="bullet">
        ///   <item>First tick: adds the component (returns Running).</item>
        ///   <item>Subsequent ticks: updates only changed fields; increments
        ///     <see cref="EqsSensor.Epoch"/> when any param changes so the solver
        ///     can discard stale in-flight results.</item>
        ///   <item>Always returns Running — the deactivator cleans up on abort.</item>
        /// </list>
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_MaintainEqsSensor(
            ref EqsParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            if (!ctx.World.HasComponent<EqsSensor>(ctx.Self))
            {
                ctx.World.AddComponent(ctx.Self, new EqsSensor
                {
                    BlueprintId          = p.BlueprintId,
                    Epoch                = 1,
                    SearchRadius         = p.SearchRadius,
                    FactionFilter        = p.FactionFilter,
                    ThreatThreshold      = p.ThreatThreshold,
                    ScoreDeltaThreshold  = p.ScoreDeltaThreshold,
                    ContextSlot0         = p.ContextSlot0,
                    ContextSlot1         = p.ContextSlot1,
                    ContextSlot2         = p.ContextSlot2,
                });
                return NodeStatus.Running;
            }

            ref var sensor = ref ctx.World.GetComponentRW<EqsSensor>(ctx.Self);
            if (sensor.BlueprintId    != p.BlueprintId    ||
                sensor.SearchRadius   != p.SearchRadius   ||
                sensor.FactionFilter  != p.FactionFilter  ||
                sensor.ThreatThreshold != p.ThreatThreshold ||
                sensor.ScoreDeltaThreshold != p.ScoreDeltaThreshold ||
                !sensor.ContextSlot0.Equals(p.ContextSlot0) ||
                !sensor.ContextSlot1.Equals(p.ContextSlot1) ||
                !sensor.ContextSlot2.Equals(p.ContextSlot2))
            {
                sensor.BlueprintId         = p.BlueprintId;
                sensor.SearchRadius        = p.SearchRadius;
                sensor.FactionFilter       = p.FactionFilter;
                sensor.ThreatThreshold     = p.ThreatThreshold;
                sensor.ScoreDeltaThreshold = p.ScoreDeltaThreshold;
                sensor.ContextSlot0        = p.ContextSlot0;
                sensor.ContextSlot1        = p.ContextSlot1;
                sensor.ContextSlot2        = p.ContextSlot2;
                sensor.Epoch++;
            }

            return NodeStatus.Running;
        }

        /// <summary>
        /// Deactivator for <see cref="Action_MaintainEqsSensor"/>.
        /// Removes both <see cref="EqsSensor"/> and <see cref="EqsCognitiveBuffer"/> when
        /// the owning sub-tree is aborted so that stale results cannot accumulate.
        /// </summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.EqsLifecycleNodes.Action_MaintainEqsSensor@0")]
        public static void Deactivate_MaintainEqsSensor(
            ref EqsParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            if (ctx.World.HasComponent<EqsSensor>(ctx.Self))
                ctx.World.RemoveComponent<EqsSensor>(ctx.Self);

            if (ctx.World.HasComponent<EqsCognitiveBuffer>(ctx.Self))
                ctx.World.RemoveComponent<EqsCognitiveBuffer>(ctx.Self);
        }

        // ── Action_WaitForSensor ──────────────────────────────────────────────

        /// <summary>
        /// Polling action that waits until the entity's <see cref="EqsCognitiveBuffer"/>
        /// is populated with at least one solver result.
        ///
        /// <list type="bullet">
        ///   <item>No buffer present, or <see cref="EqsCognitiveBuffer.IsReady"/> is false:
        ///     returns Running.</item>
        ///   <item><see cref="EqsCognitiveBuffer.IsReady"/> is true: returns Success.</item>
        /// </list>
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_WaitForSensor(
            ref EqsParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            if (!ctx.World.HasComponent<EqsCognitiveBuffer>(ctx.Self))
                return NodeStatus.Running;

            ref readonly var buffer = ref ctx.World.GetComponentRO<EqsCognitiveBuffer>(ctx.Self);
            return buffer.IsReady ? NodeStatus.Success : NodeStatus.Running;
        }

        // ── Action_SpawnEqsSensorChild ────────────────────────────────────────

        /// <summary>
        /// Finds an existing child entity whose <see cref="PartMetadata"/> matches
        /// <paramref name="parent"/> and <paramref name="instanceId"/>.
        /// Used only on first entry or after a BTree restart when the blackboard handle is empty.
        /// </summary>
        private static Entity FindExistingChild(ISimulationView world, Entity parent, int instanceId)
        {
            // Build a fresh query each time -- EntityQuery caches internal component-array pointers;
            // reusing it across structural mutations (add/remove components, entity creation) risks
            // an AccessViolationException when the underlying array is reallocated.
            // FindExistingChild is called at most once per BTree restart (idle-state guard above
            // short-circuits on subsequent ticks), so the allocation cost is negligible.
            var query = world.Query().With<PartMetadata>().Build();
            foreach (var candidate in query)
            {
                var meta = world.GetComponentRO<PartMetadata>(candidate);
                if (meta.ParentEntity.Equals(parent) && meta.InstanceId == instanceId)
                    return candidate;
            }
            return Entity.Null;
        }

        /// <summary>
        /// Persistent action that spawns a child sensor entity via the deferred command buffer.
        ///
        /// <list type="bullet">
        ///   <item>First tick: spawns the child via ECB; stores a placeholder handle.</item>
        ///   <item>Second tick: placeholder is invalid; <c>FindExistingChild</c> locates the real
        ///     entity created by ECB playback and caches its handle.</item>
        ///   <item>Subsequent ticks: handle is valid and entity is alive; returns Success immediately
        ///     (no ECS scan in steady state).</item>
        /// </list>
        ///
        /// <para>The deactivator <see cref="Deactivate_SpawnEqsSensorChild"/> destroys the child
        /// via ECB when the enclosing sub-tree is aborted.</para>
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_SpawnEqsSensorChild(
            ref EqsSpawnParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            // Deterministic LocalChildIndex: stable across ticks for the same (parent, slot) pair.
            int localChildIndex = (int)(((uint)ctx.Self.Index << 8) | p.ChildSlotIndex);

            // Idempotency: if previously spawned and still alive, reuse existing handle.
            if (p.SpawnedHandle.IsValid && ctx.World.IsAlive(p.SpawnedHandle.ChildId))
                return NodeStatus.Success;

            // Fallback idempotency scan on first entry (or after a BTree restart that cleared the
            // blackboard). Uses a cached module-level query -- never rebuilt per tick.
            Entity existingChild = FindExistingChild(ctx.World, ctx.Self, localChildIndex);
            if (!existingChild.IsNull)
            {
                p.SpawnedHandle = new EqsSensorHandle(existingChild);
                return NodeStatus.Success;
            }

            // Spawn new child via ECB (deferred structural mutation -- BTree runs in Simulation phase).
            var ecb   = ((ISimulationView)ctx.World).GetCommandBuffer();
            var child = ecb.CreateEntity();

            ecb.AddComponent(child, new PartMetadata
            {
                ParentEntity      = ctx.Self,
                InstanceId        = localChildIndex,
                DescriptorOrdinal = 0,
            });
            ecb.AddComponent(child, new EqsSensor
            {
                BlueprintId         = p.SensorConfig.BlueprintId,
                Epoch               = 1,
                SearchRadius        = p.SensorConfig.SearchRadius,
                FactionFilter       = p.SensorConfig.FactionFilter,
                ThreatThreshold     = p.SensorConfig.ThreatThreshold,
                ScoreDeltaThreshold = p.SensorConfig.ScoreDeltaThreshold,
                ContextSlot0        = p.SensorConfig.ContextSlot0,
                ContextSlot1        = p.SensorConfig.ContextSlot1,
                ContextSlot2        = p.SensorConfig.ContextSlot2,
            });
            ecb.AddComponent(child, default(EqsCognitiveBuffer));

            p.SpawnedHandle = new EqsSensorHandle(child);
            return NodeStatus.Success;
        }

        /// <summary>
        /// Deactivator for <see cref="Action_SpawnEqsSensorChild"/>.
        /// Destroys the child entity via ECB when the owning sub-tree is aborted.
        /// </summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.EqsLifecycleNodes.Action_SpawnEqsSensorChild@0")]
        public static void Deactivate_SpawnEqsSensorChild(
            ref EqsSpawnParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            if (p.SpawnedHandle.IsValid && ctx.World.IsAlive(p.SpawnedHandle.ChildId))
            {
                var ecb = ((ISimulationView)ctx.World).GetCommandBuffer();
                ecb.DestroyEntity(p.SpawnedHandle.ChildId);
            }
            p.SpawnedHandle = default;
        }

        // ── Action_WaitForChildSensor ─────────────────────────────────────────

        /// <summary>
        /// Polling action that waits until the child sensor entity's
        /// <see cref="EqsCognitiveBuffer"/> is populated with at least one solver result.
        ///
        /// <list type="bullet">
        ///   <item>Child not yet spawned or not alive: returns Running.</item>
        ///   <item>No buffer present or <see cref="EqsCognitiveBuffer.IsReady"/> is false:
        ///     returns Running.</item>
        ///   <item><see cref="EqsCognitiveBuffer.IsReady"/> is true: returns Success.</item>
        /// </list>
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_WaitForChildSensor(
            ref EqsSpawnParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            if (!p.SpawnedHandle.IsValid || !ctx.World.IsAlive(p.SpawnedHandle.ChildId))
                return NodeStatus.Running;
            if (!ctx.World.HasComponent<EqsCognitiveBuffer>(p.SpawnedHandle.ChildId))
                return NodeStatus.Running;
            ref readonly var buf = ref ctx.World.GetComponentRO<EqsCognitiveBuffer>(p.SpawnedHandle.ChildId);
            return buf.IsReady ? NodeStatus.Success : NodeStatus.Running;
        }
    }
}
