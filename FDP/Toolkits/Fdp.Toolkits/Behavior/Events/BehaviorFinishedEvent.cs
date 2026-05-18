using Fdp.Core;
using Fbt;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// Notification published by <see cref="Systems.BTreeTickSystem"/> when the behavior's
    /// BTree root evaluates to a terminal state (<see cref="NodeStatus.Success"/> or
    /// <see cref="NodeStatus.Failure"/>). Flows <b>bottom-up</b> from the Cognitive tier to
    /// the Mission tier.
    ///
    /// This event does NOT itself change any state. It is consumed by
    /// <see cref="Systems.MissionDirectorSystem"/> to drive phase-trigger evaluation.
    ///
    /// <para>
    /// <b>Tier boundary:</b> <see cref="Systems.LocomotionDispatcherSystem"/> must NOT
    /// publish this event. It operates at the action level (individual BTree leaf nodes),
    /// not at the behavior level (BTree root). Only <see cref="Systems.BTreeTickSystem"/>
    /// observes the full-tree root result and is the correct publisher.
    /// </para>
    /// </summary>
    [EventId(BehaviorConstants.EventId_BehaviorFinished)]
    public struct BehaviorFinishedEvent
    {
        /// <summary>The entity whose behavior has completed.</summary>
        public Entity Entity;

        /// <summary>The terminal result of the behavior's BTree root (<see cref="NodeStatus.Success"/> or <see cref="NodeStatus.Failure"/>).</summary>
        public NodeStatus Result;
    }
}
