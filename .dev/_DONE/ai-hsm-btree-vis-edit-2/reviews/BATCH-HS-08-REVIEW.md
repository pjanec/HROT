# BATCH-HS-08 Review — TASK-HS-08 authoring-loop round-trip (headless portion)

**Reviewer:** Dev Lead · **Date:** 2026-06-13 · **Status:** ✅ APPROVED · **Impl:** Zoo

## Verification (independent — read test, re-ran suite)
- **Real persistence path used:** Save = `HsmAssetMapper.ToDto(asset)` → `HsmJsonServices.Serialize`; Open = `HsmJsonServices.Deserialize` → `HsmAssetMapper.ToModel`. No hand-rolled serializer — the actual editor save/open path.
- **Authors via the command sink** (exercises HS-01..04 together): AddNode ×4 (incl. Final), ChangeParent (forms composite), AddLink (transition via hidden pins), SetContainerCollapsed, MoveNodes. Then save → reopen → assert.
- **Asserts preserved:** state/transition counts; every StableId + Name + all kind flags; parent/child topology + `Kind==Composite`; child's `Parent` ref; transition VisualId + Source/Target StableIds + **no dangling refs**; layout `Position` ×4 + `IsCollapsed==true`. Second test: Starter recipe round-trips (single initial state, parent, position). Values/refs/flags, not strings.
- **No persistence gaps found** — IsCollapsed, Position, StableId, VisualId, topology all survive (worker would have stopped+reported a gap per the batch; none arose).
- **No cheating / scope clean:** only the new test file added; `EditorSubsystem.cs` + window classes untouched (VE-DEBT-005 deferral honored).
- **Re-run:** `Hrot.Hsm.Editor.Tests` **456/0** (2 new, 0 pre-existing failures).

## Issues
- Events/Globals window registration deferred → **VE-DEBT-005** (asset-bound windows need a doc-retarget adapter; composition-root design change). Container/transition/label/history **rendering** = REVIEW-HS visual gate.

## Verdict
APPROVED. The full HSM authoring loop (create→edit→save→reopen) holds end-to-end on fresh content with topology + layout preserved. Completes the headless scope of Phase B.

## Commit message
```
test(hsm-editor): create-edit-save-reopen authoring round-trip (BATCH-HS-08 / TASK-HS-08)

Add an end-to-end headless test proving the HSM authoring loop: build a machine
via HsmCommandSink (states, reparent→composite, transition, collapse, moves),
save through the real path (HsmAssetMapper.ToDto + HsmJsonServices.Serialize),
reopen (Deserialize + ToModel), and assert topology, transition refs, collapse,
and positions all survive with no dangling references. +Starter-recipe round-trip.
No persistence gaps found. (Events/Globals window wiring deferred → VE-DEBT-005.)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
