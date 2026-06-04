# MVE-BATCH-06: observe a running blueprint's working-state in the editor (debug-observe)
Make the loop VISIBLE: show the selected entity's attached-blueprint live working-state (e.g. `Count`) in the editor's runtime inspector. **Golden-risk-free** route (reuse the compiler's DebugMap; NO codegen change).

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`; `.dev/blueprint-mve/DESIGN.md`; the MVE-BATCH-06 investigation findings (below).
2. Reuse (verified, with file:line):
   - `BlueprintDebugSession` (`Hrot.Blueprints.Editor/BlueprintDebugSession.cs`): already reads live `BlueprintBlackboard*` slots via `BlueprintBlackboardPartitions.TryGetSlotOffset` + `ReadInstanceState` using `DebugMap.StateLayout`; `CaptureStateSnapshot(self, assetId)` (~457-492) builds a `BlueprintStateSnapshot{FieldValues}`. `GetCurrentStateSnapshot()` (~451-455) is **pause-gated** (the gap). `RegisterDebugMap` exists + is tested. AiPrimitive path already falls back to `def.StateFields` (~523-532).
   - Compile produces the `DebugMap` (`Stage7_Emit.Run` returns `(source, DebugMap)`); `QuickReloadService` (wired in MVE-05) was constructed with `session: _blueprintDebugSession` — so it can register the map after compiling. Production currently NEVER calls `RegisterDebugMap` → `_debugMaps` empty → no fields read.
   - Runtime inspector: `RuntimeInspectorWindow.RegisterPane(IRuntimeInspectorPane)`; per-perspective `_blueprintRegistrar.RuntimeInspector` exists (no Blueprint pane registered). Templates: `BTreeRuntimeInspectorPane`/`HsmRuntimeInspectorPane`; BTree/HSM panes registered at `EditorSubsystem.cs:~1899-1910` (`SetSession(_xxxDebugSession)` + `RegisterPane`).
   - Selected entity: the AiShared `EditorSelectionStore.SelectedEntity` (the Blueprint perspective store) + active asset id from the active `AiCanvasContext.AssetRef`.
Use codebase-memory MCP; not search_code. GizmoMap.Contracts 0.2.2; no Hrot.IG/DDS.

## Task 07-A — live (non-pause-gated) read API
On `IBlueprintDebugSession` + `BlueprintDebugSession`: add a live read, e.g. `BlueprintStateSnapshot? CaptureLiveState(Entity self, Guid assetId)` that calls the existing private `CaptureStateSnapshot(self, assetId)` directly (NO pause requirement). Reuse all existing machinery. Update the ~3 test doubles (`MockDebugSession`/`CapturingDebugSession`/`SpyDebugSession`) for the new interface member.

## Task 07-B — register the compiler DebugMap into the live session on compile
After a successful compile in `QuickReloadService` (it already holds `session: _blueprintDebugSession` from MVE-05) — and/or in the reload-completed hook — call `session.RegisterDebugMap(debugMap)` with the `DebugMap` the compile produced, so `ReadInstanceState` has the `StateLayout` (field name/offset/size) for compiled blueprints. Verify the compile result surfaces the DebugMap to QuickReloadService; wire it through. (This is what makes a *compiled* blueprint's fields readable — no codegen/golden change.)

## Task 07-C — Blueprint runtime-inspector pane
New `BlueprintRuntimeInspectorPane : IRuntimeInspectorPane` (`TargetKind => AssetKind.Blueprint`), mirror `BTreeRuntimeInspectorPane`. `Draw()` (gated on ImGui): resolve the selected `Entity` (Blueprint perspective `EditorSelectionStore.SelectedEntity`) + active blueprint asset id (`AiCanvasContext.AssetRef` → `BlueprintAsset.AssetId`), call `session.CaptureLiveState(entity, assetId)`, render the `FieldValues` (+ the latent `Cursor`) in an ImGui table; empty-state text when no entity/snapshot. Register it on `_blueprintRegistrar.RuntimeInspector` next to BTree/HSM (~1899-1910), fed `_blueprintDebugSession`. Keep the field-projection logic ImGui-free + testable.

## Task 07-D (DEFERRED — do NOT do here)
Emitting `StateFields` in the generated registrar (durable runtime self-describing contract) regenerates 5 Instance golden fixtures (additive). Out of scope for this batch; note it as the follow-up (DEBT-MVE-002). The DebugMap route (07-B) makes observe work WITHOUT it.

## Task — headless observe test
Through the real kernel (reuse the MVE-02/05 harness): compile the demo Instance blueprint via `QuickReloadService` (which now also registers its DebugMap — 07-B), attach to a self-created entity, `PumpFrames(N)`, then `session.CaptureLiveState(entity, assetId)` and assert `FieldValues["Count"] == N`. This proves compiled-blueprint observation end-to-end (the thing the editor pane shows). Assert real values.

## Success Criteria
- [ ] A Blueprint runtime-inspector pane shows the selected entity's attached-blueprint live field values; updates per frame as the sim ticks.
- [ ] Compiled blueprint fields are readable live (DebugMap registered on compile); headless test asserts `Count == N` for a compiled blueprint via `CaptureLiveState`.
- [ ] Build 0 errors; touched projects no new warnings. GizmoMap.Contracts 0.2.2. **No golden/snapshot regen** (07-D deferred).
- [ ] Green: new test(s); `EditorSubsystemBoot` filter (pane + session wiring at composition); `Hrot.Blueprints.Tests` (DEBT-006 unchanged — confirm NO emit-golden changes); `Hrot.Editor.AiShared.Tests`.
- [ ] Report at `.dev/blueprint-mve/reports/MVE-BATCH-06-REPORT.md`.

## Execution rules — YOU (the sonnet agent) run the full implement→build→test→fix loop yourself
- Verify the read/session/DebugMap/pane APIs against the code FIRST (cite file:line). Reuse `CaptureStateSnapshot`/`ReadInstanceState`/`RegisterDebugMap`/the BTree pane pattern — do NOT recompute the field layout in the editor (single source of truth = the compiler's FieldLayout via DebugMap) and do NOT change codegen. Gate ImGui; keep projection logic testable. Build + run suites yourself; reach green; never fake a pass.
- Confirm the emit-golden tests are UNCHANGED (this batch must not touch generated output).

## Report
Document: the live-read API (07-A) + test-double updates; how/where the DebugMap is registered on compile (07-B) + that it reaches the kernel's session; the pane (07-C) + registration + selected-entity/asset resolution; the headless observe test + counts; confirmation of zero golden changes; build/suite results; the deferred 07-D (codegen StateFields) + DEBT-MVE-003 (multi-coordinator) status; next step MVE hot-reload. Suggested commit message. No comprehension questions.
