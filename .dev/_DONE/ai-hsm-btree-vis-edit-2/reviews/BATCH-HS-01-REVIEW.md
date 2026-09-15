# BATCH-HS-01 Review — TASK-HS-01 create state (+ asset registration API)

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED · **Impl:** Zoo

## Verification (independent — read diffs, re-ran suite)
- **HsmAsset API:** `_allStatesList`/`_allTransitionsList` captured in ctor (the same lists `AllStates`/`AllTransitions` wrap via `AsReadOnly()`, so the public views stay live). `RegisterState/UnregisterState/RegisterTransition/UnregisterTransition` update backing list + identity maps exactly as specced; `NextFree*FlatIndex()` returns max+1 (collision-free in-session per D-HS-01). Transition mutators present but **not called** from any handler (correctly left for HS-03/04).
- **ApplyAddNode:** clean kind→flags switch (Parallel/Final/History/DeepHistory set their flag; Simple/Composite/unknown → no flags), `StableId`/`Position` from cmd, `RegisterState(state, RootState)`. No promotion code — relies on `StateNode.Kind`/`IsContainer` + existing reparent (per D-HS-01).
- **No cheating:** no suppressed diagnostics, no commented code, no weakened asserts, no excluded files. Touched only the 3 named files.
- **Tests (8, behavioral):** all 6 kind keys → correct flags + `Kind.Id`; **implicit promotion** (reparent S2 under S1 → `S1.IsContainer`+`Kind==Composite`); FlatIndex uniqueness via `FindStateByFlatIndex`; AllStates count. Assert values/flags/counts, not strings.
- **Re-run (no regenerate flag):** `Hrot.Hsm.Editor.Tests` **390/0** (8 new, 382 pre-existing, 0 pre-existing failures). Build 0 errors/0 warnings.

## Issues
None.

## Verdict
APPROVED. Foundation API in place for HS-02/03/04. States can now be created on the canvas and project into the model.

## Commit message
```
feat(hsm-editor): create-state command sink + HsmAsset registration API (BATCH-HS-01 / TASK-HS-01)

HsmCommandSink.ApplyAddNode was a stub. Add a state/transition registration API to
HsmAsset (capture the backing AllStates/AllTransitions lists; Register/Unregister
State + Transition with collision-free in-session FlatIndex assignment) and
implement ApplyAddNode: map the palette kind id to StateNode flags
(Parallel/Final/History/DeepHistory) and register the new state under RootState.
Implicit promote-to-composite is automatic via StateNode.Kind/IsContainer + the
existing reparent handler. Transition mutators are added but unused until HS-03/04.
+8 headless tests (kinds, promotion, FlatIndex uniqueness, counts).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
