# ADA-BATCH-03 Review (P1 corrective — scenario load headless)

**Verdict:** ACCEPTED after one reopen. **Reviewer:** dev lead (diff + real headless reproduce, re-run personally).

## Timeline
1. **First attempt:** fixed the storage provider (load read the empty per-node staging dir instead of NAS)
   and added a real-pipeline integration test (21/21 green). **Reopened by lead** — the unit test passed but
   the *real* `-m editor --debug-api --headless` process still returned 504 / `entityCount:0`. The test had
   masked the bug by manually injecting a `NodeHeartbeatEvent`; the real process gets none within the poll window.
2. **Second attempt (continuation):** found the real cause — `ClusterMaster` only fans out a load to nodes in
   its roster, which the in-process `ClusterSlave` populates only ~1s later via heartbeat, while the 600-frame
   poll elapsed in <1s. Fixes: (a) seed node 0 in the `ClusterMaster` roster at startup via a synthetic
   heartbeat + one tick; (b) wall-clock-bound (`Stopwatch`, 30s) the `waitForReady` poll.

## Verified independently (lead)
- **Real headless reproduce:** `POST /scenario/load {name:"test-move", waitForReady:true}` →
  `{"loaded":"test-move","awaited":true}`; `/status` → `clusterState:"OperatingEdit"`, **`entityCount:1`**;
  clean exit. (Previously 504 / 0.) ✅
- `dotnet test … --filter DebugApi` → **21/21 passed** (incl. the new real-pipeline
  `DebugApiScenarioLoadTests`). Build 0 errors.

## Diff review
- NAS storage provider for `HrotScenarioLoader` (read scenarios from shared root, not the racey staging copy);
  `ReferencePrefetchHandler` keeps the staging provider. Sound for single-process editor.
- Roster seed at startup (`EditorSubsystem.cs`) — publishes one synthetic node-0 heartbeat + ticks master once,
  mirroring what a real heartbeat does ~1s later. Reasonable for the in-process single-node editor.
- Wall-clock poll (`DebugApiHost.cs`) — replaces the 600-frame cap with a 30s `Stopwatch` bound.

## Minor note (watch, not blocking)
- The startup seed calls `_orchestrationBus.SwapBuffers()` + `_clusterMaster.Tick()` during `Initialize`.
  Verified the real run is healthy and all tests pass; if any init-ordering oddity surfaces later, this is the
  first place to look.

## Lesson reinforced
Third consecutive case where the agent's report claimed success the real process didn't have. The
**re-run-the-real-headless-reproduce gate is the arbiter** — unit-test-green ≠ deployment-works.
