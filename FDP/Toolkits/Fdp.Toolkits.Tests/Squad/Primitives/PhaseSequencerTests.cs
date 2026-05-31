using Fdp.Toolkit.Squad;
using Xunit;

namespace Fdp.Toolkit.Squad.Primitives.Tests
{
    /// <summary>
    /// P1-04: Tests for <see cref="PhaseSequencer"/>.
    /// Covers SC-P1-04-1 through SC-P1-04-3.
    /// </summary>
    public class PhaseSequencerTests
    {
        [Fact]
        public void Advance_MatchingEvent_TransitionsPhase()
        {
            // SC-P1-04-1
            SquadCognitiveState state = default;
            state.PhaseId          = 0;
            state.PhaseEnteredTick = 0;

            var table = new PhaseTransitionEntry[]
            {
                new PhaseTransitionEntry { FromPhaseId = 0, EventKind = PhaseEventKind.FarSideReached, ToPhaseId = 1 },
            };
            var events = new PhaseEvent[]
            {
                new PhaseEvent(PhaseEventKind.FarSideReached),
            };

            bool transitioned = PhaseSequencer.Advance(ref state, events, table,
                currentTick: 5u, dwellTimeoutTicks: 100u, recoveryPhaseId: 99);

            Assert.True(transitioned);
            Assert.Equal(1, state.PhaseId);
            Assert.Equal(5u, state.PhaseEnteredTick);
        }

        [Fact]
        public void Advance_DwellTimeout_TransitionsToRecovery()
        {
            // SC-P1-04-2
            SquadCognitiveState state = default;
            state.PhaseId          = 0;
            state.PhaseEnteredTick = 0;

            var table  = ReadOnlySpan<PhaseTransitionEntry>.Empty;
            var events = ReadOnlySpan<PhaseEvent>.Empty;

            bool transitioned = PhaseSequencer.Advance(ref state, events, table,
                currentTick: 101u, dwellTimeoutTicks: 100u, recoveryPhaseId: 99);

            Assert.True(transitioned);
            Assert.Equal(99, state.PhaseId);
        }

        [Fact]
        public void Advance_VetoDetected_OverridesOtherEvents()
        {
            // SC-P1-04-3
            SquadCognitiveState state = default;
            state.PhaseId          = 0;
            state.PhaseEnteredTick = 0;

            var table = new PhaseTransitionEntry[]
            {
                new PhaseTransitionEntry { FromPhaseId = 0, EventKind = PhaseEventKind.FarSideReached, ToPhaseId = 1 },
            };
            // Both FarSideReached and VetoDetected in the same tick.
            var events = new PhaseEvent[]
            {
                new PhaseEvent(PhaseEventKind.FarSideReached),
                new PhaseEvent(PhaseEventKind.VetoDetected),
            };

            PhaseSequencer.Advance(ref state, events, table,
                currentTick: 5u, dwellTimeoutTicks: 1000u, recoveryPhaseId: 99);

            // VetoDetected must dominate — recovery phase wins.
            Assert.Equal(99, state.PhaseId);
        }

        // ── OFX-014: Off-by-one and zero-guard fixes ─────────────────────────────

        [Fact]
        public void Advance_AtExactDwellTick_DoesNotAdvance()
        {
            // Phase entered at tick 0; dwell = 100. At tick 100 the phase is still
            // current (strict > comparison, not >=).
            SquadCognitiveState state = default;
            state.PhaseId          = 0;
            state.PhaseEnteredTick = 0;

            var table  = ReadOnlySpan<PhaseTransitionEntry>.Empty;
            var events = ReadOnlySpan<PhaseEvent>.Empty;

            bool transitioned = PhaseSequencer.Advance(ref state, events, table,
                currentTick: 100u, dwellTimeoutTicks: 100u, recoveryPhaseId: 99);

            Assert.False(transitioned);
            Assert.Equal(0, state.PhaseId);
        }

        [Fact]
        public void Advance_OneTick_AfterDwell_DoesAdvance()
        {
            // Phase entered at tick 0; dwell = 100. At tick 101 the dwell is exceeded.
            SquadCognitiveState state = default;
            state.PhaseId          = 0;
            state.PhaseEnteredTick = 0;

            var table  = ReadOnlySpan<PhaseTransitionEntry>.Empty;
            var events = ReadOnlySpan<PhaseEvent>.Empty;

            bool transitioned = PhaseSequencer.Advance(ref state, events, table,
                currentTick: 101u, dwellTimeoutTicks: 100u, recoveryPhaseId: 99);

            Assert.True(transitioned);
            Assert.Equal(99, state.PhaseId);
        }

        [Fact]
        public void Advance_DwellTimeoutZero_NeverAdvances()
        {
            // dwellTimeoutTicks == 0 means "no timeout; only exit via events".
            SquadCognitiveState state = default;
            state.PhaseId          = 0;
            state.PhaseEnteredTick = 0;

            var table  = ReadOnlySpan<PhaseTransitionEntry>.Empty;
            var events = ReadOnlySpan<PhaseEvent>.Empty;

            bool transitioned = PhaseSequencer.Advance(ref state, events, table,
                currentTick: 99999u, dwellTimeoutTicks: 0u, recoveryPhaseId: 99);

            Assert.False(transitioned);
            Assert.Equal(0, state.PhaseId);
        }
    }
}
