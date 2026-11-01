using Fbt;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests.TriggerPoc
{
    public sealed unsafe partial class TriggerPocTests
    {
        // ══ P6 — RESTORE MID-STEP (cheap path: EntityRepository.SyncFrom = the engine's snapshot/restore primitive) ═══

        [Fact]
        public void P6_ASnapshotTakenMidStep_ResumesAtTheSameStep_WithItsWorkingState()
        {
            using var live = NewWorld();
            var e = Unit(live, ThreeSteps);                                      // record 1 · wait 3 · record 2
            Assert.Equal(NodeStatus.Running, PocOuter.Tick(live, e, 0, out _));  // record 1 done, wait 1/3
            Assert.Equal(NodeStatus.Running, PocOuter.Tick(live, e, 0, out _));  // wait 2/3

            using var restored = NewWorld();
            restored.SyncFrom(live);                                             // ← the snapshot / restore
            live.GetComponentRW<PocLog>(e).Count = 99;                           // the live world moves on / is clobbered: the copy must not care

            Assert.True(PocOuter.Holds(restored, e, PocOuter.OuterSlotKey));     // outer + step slots came across as bytes
            Assert.True(PocOuter.Holds(restored, e, PocActions.StepSlotKey));
            var (p, s) = PeekWait(restored, e);
            Assert.Equal((3, 2), (p.Ticks, s.Elapsed));                          // the step's working state survived the restore

            Assert.Equal(NodeStatus.Success, PocOuter.Tick(restored, e, 0, out var fin));   // ONE more tick finishes it: it did NOT restart the wait
            var log = restored.GetComponentRO<PocLog>(e);
            Assert.Equal((2, 1, 2), (log.Count, log.V0, log.V1));                // record 1 not repeated, record 2 ran once
            Assert.Equal(3, fin.Index);
        }
    }
}
