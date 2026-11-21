# BF-BATCH-INSPECTOR-FIELDS: Runtime inspector shows Instance state fields (e.g. Count) for latent blueprints
**Single objective. Est:** ~5h   **Dependencies:** BF-BATCH-SEQ2 (committed 2ddbc230).

## The objective
The runtime inspector (`BlueprintRuntimeInspectorPane`) shows the latent cursor for a latent Instance blueprint but
prints **"(no state fields — 07-D deferred)"** instead of the state variable values (e.g. `Count`). The user
previously saw `Count` (for a non-latent Instance blueprint) and uses it as a live "is it incrementing" check.
**Goal: the inspector displays Instance state variable values (Count) for latent Instance blueprints too.**

## What's already known (don't re-derive — verify then fix the gap)
- The inspector only prints the "deferred" message when `snapshot.FieldValues` is **empty**
  (`BlueprintRuntimeInspectorPane.DrawFieldsTable`, ~line 93). It has a working field table otherwise.
- Field values are read in `BlueprintDebugSession.ReadInstanceState` (`BlueprintDebugSession.cs:573`): it reads the
  cursor from `payloadOffset` (works — cursor shows), then at ~line 587 does
  `if (stateLayout == null || stateLayout.Fields.Count == 0) return;` — i.e. it bails (no fields) when the
  **DebugMap StateLayout** for this blueprint is missing/empty. The fields come from `mapIndex?.StateLayout.Fields`,
  NOT the live registrar.
- The compiler **does** populate the StateLayout for Instance dispatch: `CSharpEmitter.Emit` (~line 72-80) adds a
  `StateLayoutField(name, type, field.Offset, field.Size)` for each `asset.Variables`. For a latent blueprint the
  generated State struct is `{ BlueprintLatentCursor Cursor; <vars> }`, so `Count`'s offset should be **16**.

So the field is *likely* in the compiled DebugMap; the gap is most likely **editor-side**: the DebugMap for the
running instance isn't loaded/matched (e.g. Quick Reload writes no debug map; or a `StructureHash`/`blueprintId`
mismatch in the `mapIndex` lookup), so `stateLayout` is null at runtime → no fields.

## Diagnose in this order, then fix the actual gap (one fix)
1. **Compiler invariant (headless, write the test regardless):** compile a **latent Instance** blueprint with a
   variable (Sequence(Then0 → SetVariable Count=Count+1, Then1 → Delay) + a `Count:int` variable) via
   `BlueprintCompiler.Compile` and inspect the resulting `DebugMap.StateLayout.Fields`. Assert it contains `Count`
   with `OffsetBytes == 16` (after the cursor) and correct size. Also a **non-latent** Instance case → `Count` at
   `OffsetBytes == 0`. If this FAILS, the bug is compiler-side (offset/var omission) — fix `CSharpEmitter`/the layout
   so latent Instance vars get the post-cursor offset.
2. **Reader invariant (headless):** unit-test `ReadInstanceState` (or the smallest public seam) with a synthetic
   blackboard byte buffer containing a cursor + `Count=7` at offset 16, plus a `DebugStateLayout` with `Count@16:int`.
   Assert the returned `FieldValues["Count"] == 7`. If this FAILS, fix the reader.
3. **Editor load/match (most likely the real gap; harder to unit-test):** determine why `mapIndex`/`StateLayout` is
   null at runtime for the running instance. Check: does **Quick Reload** (`QuickReloadService`) register/update the
   DebugMap (with StateLayout) the same way **Full Rebuild** does? Is the `DebugMapIndex` lookup keyed by something
   that mismatches (StructureHash/blueprintId) after an edit? Fix so that after the normal editor flow the DebugMap
   (incl. StateLayout) for the current blueprint is available to `CaptureLiveState`. Keep the cursor read working.

## Tests required — PRESCRIBED
- `DebugMap_LatentInstance_StateLayoutHasVarAtPostCursorOffset` — exactly invariant #1 (latent `Count@16`, non-latent
  `Count@0`).
- `ReadInstanceState_WithLayout_ReturnsFieldValue` — exactly invariant #2 (`Count==7` from a synthetic buffer+layout).
- If the fix is in the editor load/match path and a seam is unit-testable, add a test that `CaptureLiveState` (or the
  debug-map registration it depends on) yields a non-empty StateLayout for a freshly compiled+registered latent
  Instance blueprint. If the gap is genuinely only reachable through the live editor (no headless seam), say so in the
  report and provide a **precise manual verification checklist** (rebuild, attach, expect `Count` row).

## Success Criteria
- [ ] Inspector shows Instance state variable values (Count) for latent Instance blueprints (verified by the headless
      invariants above; + manual checklist if the load path isn't headless-testable).
- [ ] The two prescribed headless tests pass; cursor display still works.
- [ ] Full `Hrot.Blueprints.Tests` suite green except the one documented pre-existing
      `TickFrame_1000Frames_AllocatesZeroBytes` (do not touch it).
- [ ] Report at `.dev/_DONE/blueprint-finalize/reports/BF-BATCH-INSPECTOR-FIELDS-REPORT.md` stating which of the 3 was the
      actual gap, with evidence.

## DO NOT STOP UNTIL VERIFIED GREEN
Run `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` (no `BLUEPRINT_REGENERATE_SNAPSHOTS`) yourself;
not done until `Failed: 0` (except the documented zero-alloc). On any other failure: diagnose, fix, re-run the whole
suite; loop until green. Do not report complete with red tests. End the report with the green suite output.

## Guardrails
One objective only (inspector shows Instance state fields). Do NOT modify other batches' committed files, edit user
blueprint assets, suppress diagnostics, weaken assertions, or change the "07-D" message into a fake value. If the
field genuinely cannot be read, leave the honest message — but first do the real fix. Read `.dev/.guides/DEV-GUIDE.md`.
