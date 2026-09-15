# BF-BATCH-EXECFANOUT Review
**Status:** ✅ APPROVED (code+tests) — asset edits withheld for user decision   **Date:** 2026-06-09

## Summary
EXEC1 (Stage2 `V_ExecOutFanOut` → BP1411 hard error on >1 successor per exec-out pin) and EXEC2 (editor
replace-on-reconnect for exec-out, via validator `InvalidReplace` + sink `RemoveExistingExecOutLink`) are implemented
per spec, with genuinely behavioral tests. Independently verified: 1623 passed / 4 pre-existing fail / 8 skipped.

## Issues Found
### Issue 1 (P2 → not a code defect): agent neutered the user's experiment assets to get a green build
**Files:** `Counting.bp.json` (DemoSharedAction exec link removed → node now dead), `EnumDemo.bp.json` (untracked; 3
fan-out links removed → 3 dead nodes), plus a helper `format_bp_json.csx`.
**Problem:** BP1411-as-build-error fires inside the source generator, so the user's working-tree assets broke the
build. The agent fixed the build by **disconnecting** the demo action nodes — which defeats the user's actual goal
(make DemoSharedAction emit code). The correct authoring is a **Sequence node** (run both the count chain *and*
DemoSharedAction).
**Fix:** Do NOT commit the asset disconnects. Commit code+tests only. Surface to the user; offer to rewire
`Counting.bp.json` with a Sequence node and verify the generated `.g.cs` contains the DemoSharedAction call.

## Design consequence to flag (not a defect)
BP1411 is a **hard error emitted during `dotnet build`** (via `BlueprintIncrementalGenerator`). Therefore **any**
`.bp.json` present in a compiled assets folder with an exec-out fan-out will break the whole build. With EXEC2 in
place the editor can no longer *create* a fan-out (new wire replaces the old), so BP1411 mainly catches legacy /
hand-edited assets — acceptable as a loud guard, but it has teeth.

## Test Quality
Good. Tests run the real `Stage2_Validate.Run` and `sink.Apply`/`validator.Validate` and assert behavior: BP1411
code+severity, per-pin no-false-positive (SequenceNode), exec-out replace leaves exactly one link to the new target,
data-input replacement unregressed, exec-in fan-in preserved. They would fail if the implementation were broken.

## Verdict
APPROVED for the 4 source files + 2 test files. Asset edits (`Counting.bp.json`, `EnumDemo.bp.json`,
`format_bp_json.csx`) excluded from the commit pending the user's rewire decision.

## Commit Message
```
fix(blueprints): exec-out fan-out guard — BP1411 compiler error + editor replace-on-reconnect (BF-BATCH-EXECFANOUT)

Completes EXEC1, EXEC2.
- EXEC1: Stage2 V_ExecOutFanOut emits BP1411 (Error) when an exec-out pin drives >1 successor
  (per-pin, so Branch/Sequence/When multi-out nodes don't false-positive); turns the scheduler's
  silent first-wins drop into a hard, locatable failure.
- EXEC2: exec-out is now 1:1 in the editor — BlueprintLinkValidator signals InvalidReplace for an
  already-connected exec-out; BlueprintCommandSink.RemoveExistingExecOutLink removes the prior wire
  by source pin. Exec-in fan-in and data-input replacement preserved.
Tests: 9 new (4 Stage2 validator, 5 editor validator/sink); full Blueprints suite 1623 pass / 4 pre-existing fail.
```
