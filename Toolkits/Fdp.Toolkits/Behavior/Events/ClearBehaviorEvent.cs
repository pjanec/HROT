using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// Imperative command published by higher-level systems to forcibly clear the active behavior
    /// on an entity, putting it into a "brain-dead" state (no active behavior, channels reset).
    /// Flows <b>top-down</b> into the Cognitive tier and is consumed by
    /// <see cref="Systems.BehaviorIngressSystem"/>.
    ///
    /// <para>
    /// Publishers: <see cref="Systems.MissionDirectorSystem"/> (end of plan) and
    /// <c>Hrot.SimHost.Systems.MissionControlRequestSystem</c> (<c>CMD_ABORT_ALL</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Distinction from <see cref="BehaviorFinishedEvent"/>:</b> This is a forced reset,
    /// not a natural completion. The behavior was interrupted or the plan exhausted; other
    /// systems may need to distinguish natural vs. forced termination in future increments.
    /// </para>
    /// </summary>
    [EventId(BehaviorConstants.EventId_ClearBehavior)]
    public struct ClearBehaviorEvent
    {
        /// <summary>The entity whose behavior should be cleared.</summary>
        public Entity Entity;
    }
}
