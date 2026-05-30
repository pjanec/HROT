using Fdp.Toolkit.Squad;

namespace Fdp.Toolkit.Squad.Primitives
{
    /// <summary>
    /// Kinds of completion events that can drive squad-HSM phase transitions.
    /// </summary>
    public enum PhaseEventKind : byte
    {
        ShotFired          = 0,
        DefiladeReached    = 1,
        FarSideReached     = 2,
        BoundComplete      = 3,
        VetoDetected       = 4,   // always routes to recovery phase, overrides other events
        Abort              = 5
    }

    /// <summary>One squad phase completion event.</summary>
    public struct PhaseEvent
    {
        public PhaseEventKind Kind;
        public PhaseEvent(PhaseEventKind kind) { Kind = kind; }
    }

    /// <summary>
    /// One entry in a per-maneuver transition table:
    /// when in <see cref="FromPhaseId"/> and <see cref="EventKind"/> fires,
    /// transition to <see cref="ToPhaseId"/>.
    /// </summary>
    public struct PhaseTransitionEntry
    {
        public ushort         FromPhaseId;
        public PhaseEventKind EventKind;
#pragma warning disable CS0169
        private byte          _pad;
#pragma warning restore CS0169
        public ushort         ToPhaseId;
    }

    /// <summary>
    /// Drives the squad HSM: processes completion events and dwell-timeout against
    /// a caller-supplied transition table, updating <see cref="SquadCognitiveState.PhaseId"/>
    /// and <see cref="SquadCognitiveState.PhaseEnteredTick"/> (design §2 primitive 4, §9).
    /// </summary>
    public static class PhaseSequencer
    {
        /// <summary>
        /// Advances the phase state machine.
        /// </summary>
        /// <param name="state">Squad cognitive state to read/write.</param>
        /// <param name="events">
        ///   Completion events for this tick, processed in span order.
        ///   <see cref="PhaseEventKind.VetoDetected"/> always overrides other events and
        ///   routes to <paramref name="recoveryPhaseId"/>.
        /// </param>
        /// <param name="table">Per-maneuver transition table.</param>
        /// <param name="currentTick">Current simulation tick.</param>
        /// <param name="dwellTimeoutTicks">
        ///   If no completion event fires and
        ///   <c>currentTick - state.PhaseEnteredTick >= dwellTimeoutTicks</c>,
        ///   transition to <paramref name="recoveryPhaseId"/>.
        /// </param>
        /// <param name="recoveryPhaseId">
        ///   Phase id to transition to on veto or dwell-timeout.
        /// </param>
        /// <returns>
        ///   <c>true</c> if a phase transition occurred this call.
        /// </returns>
        public static bool Advance(
            ref SquadCognitiveState state,
            ReadOnlySpan<PhaseEvent> events,
            ReadOnlySpan<PhaseTransitionEntry> table,
            uint currentTick,
            uint dwellTimeoutTicks,
            ushort recoveryPhaseId)
        {
            // First scan for VetoDetected — it overrides everything.
            foreach (ref readonly var ev in events)
            {
                if (ev.Kind == PhaseEventKind.VetoDetected)
                {
                    state.PhaseId          = recoveryPhaseId;
                    state.PhaseEnteredTick = currentTick;
                    return true;
                }
            }

            // Then scan events against the transition table.
            foreach (ref readonly var ev in events)
            {
                foreach (ref readonly var entry in table)
                {
                    if (entry.FromPhaseId == state.PhaseId && entry.EventKind == ev.Kind)
                    {
                        state.PhaseId          = entry.ToPhaseId;
                        state.PhaseEnteredTick = currentTick;
                        return true;
                    }
                }
            }

            // Then check dwell timeout.
            if (currentTick - state.PhaseEnteredTick >= dwellTimeoutTicks)
            {
                state.PhaseId          = recoveryPhaseId;
                state.PhaseEnteredTick = currentTick;
                return true;
            }

            return false;
        }
    }
}
