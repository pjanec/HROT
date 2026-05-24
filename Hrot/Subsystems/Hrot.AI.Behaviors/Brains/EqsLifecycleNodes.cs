using System.Runtime.InteropServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
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
                    BlueprintId      = p.BlueprintId,
                    Epoch            = 1,
                    SearchRadius     = p.SearchRadius,
                    FactionFilter    = p.FactionFilter,
                    ThreatThreshold  = p.ThreatThreshold,
                });
                return NodeStatus.Running;
            }

            ref var sensor = ref ctx.World.GetComponentRW<EqsSensor>(ctx.Self);
            if (sensor.BlueprintId    != p.BlueprintId    ||
                sensor.SearchRadius   != p.SearchRadius   ||
                sensor.FactionFilter  != p.FactionFilter  ||
                sensor.ThreatThreshold != p.ThreatThreshold)
            {
                sensor.BlueprintId     = p.BlueprintId;
                sensor.SearchRadius    = p.SearchRadius;
                sensor.FactionFilter   = p.FactionFilter;
                sensor.ThreatThreshold = p.ThreatThreshold;
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
    }
}
