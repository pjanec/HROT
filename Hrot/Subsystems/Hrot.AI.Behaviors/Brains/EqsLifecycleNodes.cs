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
using Fbt.Kernel;

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
        [SharedAiAction]
        public static NodeStatus Action_MaintainEqsSensor(ref EqsParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<EqsSensor>(self))
            {
                world.AddComponent(self, new EqsSensor
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

            ref var sensor = ref world.GetComponentRW<EqsSensor>(self);
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
                sensor.Epoch = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.NextEpoch(sensor.Epoch);   // CE-3049 — never carry into the owner stamp
            }

            return NodeStatus.Running;
        }

        /// <summary>
        /// Deactivator for <see cref="Action_MaintainEqsSensor"/>.
        /// Removes both <see cref="EqsSensor"/> and <see cref="EqsCognitiveBuffer"/> when
        /// the owning sub-tree is aborted so that stale results cannot accumulate.
        /// </summary>
        // ⭐ CE-504 slice 4 — the shared param-less deactivator form; paired with its action by method FQN, per binding.
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.EqsLifecycleNodes.Action_MaintainEqsSensor")]
        public static void Deactivate_MaintainEqsSensor(Entity self, EntityRepository world)
        {
            if (world.HasComponent<EqsSensor>(self))
                world.RemoveComponent<EqsSensor>(self);

            if (world.HasComponent<EqsCognitiveBuffer>(self))
                world.RemoveComponent<EqsCognitiveBuffer>(self);
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
        [SharedAiAction]
        public static NodeStatus Action_WaitForSensor(ref EqsParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<EqsCognitiveBuffer>(self))
                return NodeStatus.Running;

            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(self);
            return buffer.IsReady ? NodeStatus.Success : NodeStatus.Running;
        }

        // ── Action_SpawnEqsSensorChild ────────────────────────────────────────

        /// <summary>
        /// Persistent action that ensures this node's child sensor exists.
        ///
        /// <para>⭐ <b>CE-485</b> (📄 <c>DESIGN_Behaviour_Fault_And_Teardown.md</c> §1 D5): routed through
        /// <see cref="EqsChildSensor.Ensure"/> — the site is <see cref="EqsSpawnParams.ChildSlotIndex"/>, the part id (DDS key)
        /// is allocated and reused, and the sensor is stamped with the behaviour run, which destroys it when it ends. ⛔ The old
        /// <c>(Self.Index &lt;&lt; 8) | slot</c> part id is retired (it grew with the world and overflowed). ⭐ On the live world
        /// the child exists at once, so <see cref="EqsSpawnParams.SpawnedHandle"/> is a real entity on the creating tick —
        /// closes <c>CE-481</c> (it used to publish an ECB placeholder).</para>
        ///
        /// <para>The deactivator <see cref="Deactivate_SpawnEqsSensorChild"/> destroys the child when the enclosing sub-tree is
        /// aborted.</para>
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Action_SpawnEqsSensorChild(ref EqsSpawnParams p, Entity self, EntityRepository world)
        {
            // Idempotency: if previously spawned and still alive, reuse existing handle.
            if (p.SpawnedHandle.IsValid && world.IsAlive(p.SpawnedHandle.ChildId))
                return NodeStatus.Success;

            var child = EqsChildSensor.Ensure(world, self, p.ChildSlotIndex, new EqsSensor
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
            p.SpawnedHandle = new EqsSensorHandle(child);   // Null only on a deferred (non-live) view
            return NodeStatus.Success;
        }

        /// <summary>
        /// Deactivator for <see cref="Action_SpawnEqsSensorChild"/>.
        /// Destroys the child entity via ECB when the owning sub-tree is aborted.
        /// </summary>
        // ⭐ CE-504 slice 4 — the shared plain deactivator form: projected at the same offset as its action's binding.
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.EqsLifecycleNodes.Action_SpawnEqsSensorChild")]
        public static void Deactivate_SpawnEqsSensorChild(ref EqsSpawnParams p, Entity self, EntityRepository world)
        {
            if (p.SpawnedHandle.IsValid && world.IsAlive(p.SpawnedHandle.ChildId))
            {
                var ecb = ((ISimulationView)world).GetCommandBuffer();
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
        [SharedAiAction]
        public static NodeStatus Action_WaitForChildSensor(ref EqsSpawnParams p, Entity self, EntityRepository world)
        {
            if (!p.SpawnedHandle.IsValid || !world.IsAlive(p.SpawnedHandle.ChildId))
                return NodeStatus.Running;
            if (!world.HasComponent<EqsCognitiveBuffer>(p.SpawnedHandle.ChildId))
                return NodeStatus.Running;
            ref readonly var buf = ref world.GetComponentRO<EqsCognitiveBuffer>(p.SpawnedHandle.ChildId);
            return buf.IsReady ? NodeStatus.Success : NodeStatus.Running;
        }
    }
}
