using System;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Xunit;

namespace Fdp.Toolkits.Tests.Squad
{
    /// <summary>
    /// Tests for <see cref="SquadHsmShell"/>.
    /// Success criteria: SC-P6-01-1, SC-P6-01-2.
    /// </summary>
    public class SquadHsmShellTests
    {
        // ── SC-P6-01-1: DangerAreaCrossing expressed via shell preserves identical transitions ──

        [Fact]
        public void DangerAreaCrossing_ViaShell_SameTransitionsAsDirectCalls()
        {
            // Build shell from DangerAreaCrossingManeuver transition table.
            // PhaseReform is the terminal phase; use as abortPhaseId.
            var shell = new SquadHsmShell(
                DangerAreaCrossingManeuver.BuildTransitionTable(),
                abortPhaseId: DangerAreaCrossingManeuver.PhaseReform,
                dwellTimeoutTicks: 0);

            var state = default(SquadCognitiveState);
            state.PhaseId = DangerAreaCrossingManeuver.PhaseSetSecurity;
            state.PhaseEnteredTick = 0;

            // Tick 1: DefiladeReached -> CrossElement
            shell.Tick(ref state,
                new ReadOnlySpan<PhaseEvent>(new PhaseEvent[] { new PhaseEvent(PhaseEventKind.DefiladeReached) }),
                currentTick: 1);
            Assert.Equal(DangerAreaCrossingManeuver.PhaseCrossElement, state.PhaseId);

            // Tick 2: FarSideReached -> FarSideCover
            shell.Tick(ref state,
                new ReadOnlySpan<PhaseEvent>(new PhaseEvent[] { new PhaseEvent(PhaseEventKind.FarSideReached) }),
                currentTick: 2);
            Assert.Equal(DangerAreaCrossingManeuver.PhaseFarSideCover, state.PhaseId);

            // Tick 3: ShotFired -> CollapseSecurity
            shell.Tick(ref state,
                new ReadOnlySpan<PhaseEvent>(new PhaseEvent[] { new PhaseEvent(PhaseEventKind.ShotFired) }),
                currentTick: 3);
            Assert.Equal(DangerAreaCrossingManeuver.PhaseCollapseSecurity, state.PhaseId);
        }

        // ── SC-P6-01-2: Trivial 2-phase maneuver authored in < 50 lines ──

        [Fact]
        public void TrivialFormUpMoveOut_AuthoredUnder50Lines()
        {
            // Trivial maneuver: FormUp(0) -> BoundComplete -> MoveOut(1) [terminal]
            //                               -> Abort        -> Aborted(2) [terminal]
            const ushort PhaseFormUp  = 0;
            const ushort PhaseMoveOut = 1;
            const ushort PhaseAborted = 2;

            var table = new PhaseTransitionEntry[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseFormUp, EventKind = PhaseEventKind.BoundComplete, ToPhaseId = PhaseMoveOut },
                new PhaseTransitionEntry { FromPhaseId = PhaseFormUp, EventKind = PhaseEventKind.Abort,         ToPhaseId = PhaseAborted },
            };

            int onEnterMoveOutCount = 0;
            var shell = new SquadHsmShell(table, abortPhaseId: PhaseAborted)
                .OnEnter(PhaseMoveOut, () => onEnterMoveOutCount++);

            var state = default(SquadCognitiveState);
            state.PhaseId = PhaseFormUp;
            state.PhaseEnteredTick = 0;

            bool transitioned = shell.Tick(ref state,
                new ReadOnlySpan<PhaseEvent>(new PhaseEvent[] { new PhaseEvent(PhaseEventKind.BoundComplete) }),
                currentTick: 5);

            Assert.True(transitioned);
            Assert.Equal(PhaseMoveOut, state.PhaseId);
            Assert.Equal(1, onEnterMoveOutCount);
            // Above: 27 lines of setup/assert for the whole trivial maneuver -- well under 50.
        }
    }
}
