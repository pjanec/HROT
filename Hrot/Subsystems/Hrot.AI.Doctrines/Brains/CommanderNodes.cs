using System.Runtime.InteropServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Events;

namespace Hrot.AI.Doctrines.Brains
{
    /// <summary>
    /// FastBTree action node delegates for Commander AI tactical order issuance.
    /// Hot-reloadable; compiled independently into Hrot.AI.Doctrines.
    /// </summary>
    public static class CommanderNodes
    {
        // -- Typed blackboard wrappers --

        /// <summary>Typed blackboard wrapper for the IssueTacticalIntent Commander action.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct IssueTacticalIntentBlackboard { public IssueTacticalIntentParams Params; }

        /// <summary>
        /// Blackboard layout for the IssueTacticalIntent Commander action.
        /// <para>
        /// IntentId and JsonParams cannot be embedded as strings in the unmanaged blackboard.
        /// The IntentId is encoded as an integer ordinal resolved from the intent registry at
        /// tree-build time by the AiDoctrineFactory (TODO: wire registry lookup).
        /// JsonParams are pre-serialized as a fixed-length UTF-8 blob when the tree is authored
        /// (TODO: implement fixed-buffer encoding).
        /// </para>
        /// <para>
        /// For this reference implementation, the IntentId is hardcoded to the first registered
        /// intent ordinal and JsonParams is empty.
        /// </para>
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct IssueTacticalIntentParams
        {
            /// <summary>
            /// Packed ECS entity value of the subordinate to command.
            /// 0 means no subordinate has been resolved yet — action returns Failure.
            /// TODO: extend to a fixed-size list of subordinates (formation roster).
            /// </summary>
            public long SubordinatePacked;

            /// <summary>
            /// Integer ordinal of the tactical intent to issue, resolved from the intent
            /// registry at tree authoring time. Maps to a string IntentId at runtime.
            /// TODO: wire to a registered intent-type lookup table in AiDoctrineFactory.
            /// </summary>
            public int IntentTypeOrdinal;
        }

        /// <summary>
        /// BTree action node for issuing a tactical intent to a single subordinate entity.
        /// <para>
        /// Publishes <see cref="AssignTacticalIntentEvent"/> on the local event bus.
        /// The event is consumed either by <c>TacticalIntentResolutionSystem</c> (if the
        /// subordinate is local) or by <c>TacticalIntentEgressTranslator</c> (if remote).
        /// </para>
        /// <para>
        /// This is a reference implementation. See <see cref="IssueTacticalIntentParams"/>
        /// for the TODO items needed for full production use.
        /// </para>
        /// </summary>
        [BTreeAction]
        public static NodeStatus Action_IssueTacticalIntent(
            ref IssueTacticalIntentParams p,
            ref BehaviorTreeState state,
            ref BTreeContext ctx)
        {
            if (p.SubordinatePacked == 0)
                return NodeStatus.Failure;

            var subordinate = new Entity((ulong)p.SubordinatePacked);

            // TODO: resolve IntentId string from a registered intent-type lookup table
            // (keyed by p.IntentTypeOrdinal). For the reference implementation, "DefendArea"
            // is used as a compile-time constant.
            const string intentId = "DefendArea";

            ctx.World.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = subordinate,
                IntentId   = intentId,
                JsonParams = string.Empty
            });

            return NodeStatus.Success;
        }
    }
}
