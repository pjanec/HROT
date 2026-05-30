using System;
using System.Collections.Generic;
using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad
{
    /// <summary>
    /// Lightweight authoring shell over <see cref="PhaseSequencer"/>.
    ///
    /// Usage:
    /// 1. Build a shell with the maneuver's transition table.
    /// 2. Optionally register per-phase entry callbacks with OnEnter().
    /// 3. Call Tick() each simulation step with the current event list.
    ///
    /// The shell does NOT own SquadCognitiveState -- the caller passes it by ref.
    /// </summary>
    public sealed class SquadHsmShell
    {
        private readonly PhaseTransitionEntry[] _table;
        private readonly Dictionary<ushort, Action> _onEnter;
        private readonly ushort _abortPhaseId;
        private readonly uint _dwellTimeoutTicks;

        /// <param name="table">Transition table (from BuildTransitionTable).</param>
        /// <param name="abortPhaseId">Phase ID to recover to on timeout. Use the terminal Aborted phase.</param>
        /// <param name="dwellTimeoutTicks">Ticks before auto-advance to abortPhaseId. 0 = never.</param>
        public SquadHsmShell(
            PhaseTransitionEntry[] table,
            ushort abortPhaseId,
            uint dwellTimeoutTicks = 0)
        {
            _table             = table;
            _onEnter           = new Dictionary<ushort, Action>();
            _abortPhaseId      = abortPhaseId;
            _dwellTimeoutTicks = dwellTimeoutTicks;
        }

        /// <summary>Register a callback to fire when a phase is entered.</summary>
        public SquadHsmShell OnEnter(ushort phaseId, Action callback)
        {
            _onEnter[phaseId] = callback;
            return this;
        }

        /// <summary>
        /// Advance the state machine one step.
        /// Returns true if a phase transition occurred.
        /// </summary>
        public bool Tick(
            ref SquadCognitiveState state,
            ReadOnlySpan<PhaseEvent> events,
            uint currentTick)
        {
            bool transitioned = PhaseSequencer.Advance(
                ref state, events, _table, currentTick,
                _dwellTimeoutTicks, _abortPhaseId);

            if (transitioned && _onEnter.TryGetValue(state.PhaseId, out var callback))
                callback();

            return transitioned;
        }
    }
}
