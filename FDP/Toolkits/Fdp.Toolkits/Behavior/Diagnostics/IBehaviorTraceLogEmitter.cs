using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>
    /// Sink for human-readable behavior trace strings emitted by the BTree/HSM tick
    /// systems when <see cref="BehaviorDebugFlags.EmitToLog"/> is set. The default
    /// implementation in <c>Hrot.AI.Behaviors</c> delegates to <c>BehaviorLog.Trace</c>;
    /// FDP-layer tick systems call <see cref="BehaviorTraceLog"/> without knowing
    /// who the concrete sink is, preserving the FDP → Hrot layer direction.
    /// </summary>
    public interface IBehaviorTraceLogEmitter
    {
        /// <summary>Returns <c>false</c> when the underlying log target is off, so the
        /// caller can skip expensive string interpolation.</summary>
        bool IsTraceEnabled { get; }

        /// <summary>Emit one fully-formatted trace line for an entity.</summary>
        void EmitTrace(Entity entity, EntityRepository repo, string message, string actionName);
    }

    /// <summary>
    /// Composition-root settable accessor that lets the FDP tick systems publish
    /// trace strings without taking a project reference on <c>Hrot.AI.Behaviors</c>.
    /// </summary>
    public static class BehaviorTraceLog
    {
        public static IBehaviorTraceLogEmitter? Instance { get; set; }
    }
}
