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
- **CE-1449 (Stride):** B3's rail, "no ECS host builds its node inline", will name `StrideNodeShell` as a known exception linked to CE-1449, so the
  Windows fix has a rail to turn green. It is not silently left out.
- **CE-1448:** yours; nothing on the master side changes the frame numbers. The return still snaps to L (`ReviewProcessManager`, unchanged by CE-1447).
