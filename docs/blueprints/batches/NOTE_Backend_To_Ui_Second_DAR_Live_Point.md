# NOTE — backend → ui: CE-1447 fixed (a second DAR's live point)

**Date:** 2026-10-29 · **From:** backend · **Tracker:** [CE-1447](../Blueprint_Issues_Tracker.md)

## What was wrong
`ReviewProcessManager` captured the live point L on the first tick the master clock reported `Deterministic`. The review's
**entry seek** also produces that mode (`SnapAndPause`), and its own `PauseTimeIntent` re-arms the 200 ms pause barrier. On a warm
second DAR, the prepare + seek round finished inside that window. The seek's snap therefore came first, and L was read at the DAR's
position (your 0.315 s instead of 8.1 s).

## The fix
L is now also captured the moment the entry `SeekReplayIntent` appears while the manager is still waiting for L. The pause has
already frozen the sim time, and the snap is still a round away. The fix is master-side only; nothing changed in the session
contract (`LivePointSimSeconds` / `LivePointSeconds`).

- Rail: `Hrot.Orchestrator.Tests/ClusterMasterReviewTests.CE1447_The_live_point_is_the_live_time_even_when_the_entry_seek_lands_before_the_barrier`, red-proved.
- Design: [`DESIGN_Replay_Time_Axis_And_Review.md`](../../DESIGN_Replay_Time_Axis_And_Review.md) §11c F22.

## Ask
Your CE-4104 rails run one DAR per run to avoid this bug. You can now take **two DARs in one run**, as the row suggested. Please run
that through the real editor; it is the end-to-end proof the unit rail cannot give.

## Re your note "Run controls landed and DAR findings" (`614ee23e0`)
- **CE-1445:** noted. CE-1446 B1/B2 keep CGF's and the editor's run controls built in one place, which becomes `NodeRunControls.For` when backend
  merges ui (after B1 commits). `TheRunChoiceIsOnEveryHostTests.CE1445_*` and `TheStartGateIsComposedOnceTests.CE1445_*` join B1/B2's gates.
- **CE-1449 (Stride):** ⛔ correction to what I wrote before. Stride's NODE is already built through `StrideNodeBootstrapper`
  (`StrideNodeShell.cs:238`), so B3's rail covers it on its merits and has no exception for it. CE-1449 is only Stride's hand-built
  run controls. That needs its own rail on `NodeRunControls.For`, which is yours.
- **CE-1448:** yours; nothing on the master side changes the frame numbers. The return still snaps to L (`ReviewProcessManager`, unchanged by CE-1447).

## Found while gating CE-1446 B1: a red in your `TheRunChoiceIsOnEveryHostTests`
Four of ten fail identically at `CreateParticipant`. It is not caused by B1: they fail the same way on a clean worktree at `9092c271a`.
Each host takes `DomainId = 230 + Interlocked.Increment(ref _domain)`, so the third host lands on domain 233. CycloneDDS's maximum is 232,
and the participant cannot be created. Suggested fix: a base that leaves room for the class's host count (ClusterRunner tests use
domains from about 100 up), or wrap within one block. ui owns the class, so backend did not touch it.

## CE-1446 is built (`2026-10-30`)
CGF (`e405d3662`) and the editor (`31f8d1b2d`) build their nodes through `CgfNodeBootstrapper` / `EditorNodeBootstrapper`; the B3 rail
(`3234e64ae`) fails on any host that builds a node inline. Both still build their run controls through `NodeRunControls.For`. Two changes
affect you:
- **The editor's frame:** `_node.Tick(...)` then `OrchestratorCore.Tick`; the editor's own per-frame work (hot-reload drain, brain
  observation, the regeneration scheduler) runs in the `afterKernel` callback.
- **The editor's system order:** it now follows the shared bootstrapper's phase order (the input, creation and genesis systems lead) and
  gains the Geographic module's two systems.

Both are pinned by `TheEditorRunsTheOrchestratorCoreTests.CE1446B2_*`. If a ui rail relies on the old order, it will show up there.
