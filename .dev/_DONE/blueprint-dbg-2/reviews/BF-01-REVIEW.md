# BF-01 Review (delegated to Zoo worker)
**Status:** ✅ APPROVED   **Date:** 2026-06-10

## Summary
Fixes the runtime-inspector crash on pause (`Component type ID 162 not found … before playback`). Root cause: `RestorePointerToScratch` seeded the scratch repo via `SyncFrom(_liveRepo)` (snapshotable-only mask), but the per-node keyframe captures **recordable** types — a `[DataPolicy(DataPolicy.NoSnapshot)]` component is recordable but not snapshotable, so the scratch never registered it. Fix: `SyncFrom(_liveRepo, includeTransient: true)` → registers all types (`GetAllIds()` ⊇ recordable).

## Verification performed (independent — Zoo: trust diffs not report)
- **Scope:** `git status` shows ONLY `BlueprintDebugSession.cs` (one-line fix + comment) and the new test file changed — no scope creep, no excluded assets, no suppressed diagnostics, no litter.
- **Fix diff** read: exactly the prescribed one-liner + explanatory comment.
- **Test** read (`SubTickRestoreRegistrationTests.cs`): genuinely reproduces the crash — `Assert.Throws<InvalidOperationException>` on the OLD `SyncFrom(repo)` + `RestoreTo` path, mask-level proof that `GetSnapshotableMask(false)` excludes the NoSnapshot type, and positive proof the NEW `includeTransient:true` seeding restores correctly (entity alive, `NormalInt==99`, `NoSnapshotProbe.V==42`). Would fail without the fix.
- **Ran full `Hrot.Blueprints.Tests` myself:** 1735 passed / 7 failed / 8 skipped / 1750 total. Same 7 documented pre-existing reds, **zero new failures**; new test passes.

## Notes / lesson
- This was a latent P1 from BATCH-03's scratch-restore: the BATCH-03/04 tests hand-registered the scratch with exactly the test's components, so they never exercised a recordable-but-not-snapshotable type — the production world (type 162) did. Future restore tests should seed the scratch from a repo containing a NoSnapshot component (now covered).
- First task delegated to the Zoo worker via the `claude-worker-orchestrator` MCP — clean result on a small, prescribed, single-objective fix.

## Verdict
APPROVED — committed.
