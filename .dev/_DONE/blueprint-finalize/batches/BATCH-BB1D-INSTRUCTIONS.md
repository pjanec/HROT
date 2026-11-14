# BATCH-BB1D: Live-wire BB1 in the composition root (EditorSubsystem)
**Tasks:** Corrective Task 0 (P1 — BB1 editor live-wiring)   **Phase:** 7 (BB1)   **Est:** ~6h
**Dependencies:** BB1A `07f56325`, BB1B `cb07da24`, BB1C `03c478f9`.

> **TOOLING:** Do NOT use the codebase-memory MCP — it HANGS. Use Grep / Glob / Read only.

## Why this batch (the #1 recurring trap, per ONBOARDING.md §"editor live-wiring gaps")
BB1A–C are headlessly complete but **invisible/non-functional in the running editor** because the composition
root `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` was never updated:
- **L1943 / L1952:** `_btreeRegistrar` / `_hsmRegistrar` are built WITHOUT `expressionTargetFieldAccessor`
  → the B-3 "Static Parameters" panel never renders.
- **L2002:** `BTreePickerDrawerFactory.BuildDrawers(btreeAsset, _behaviorRegistry)` — no `IActionSchemaExporter`,
  no `BTreeFacetFqnContext` → B-1 picker shows ALL vars (no type filter) and B-2 Promote returns null.
- **L2012:** `HsmPickerDrawerFactory.BuildDrawers(hsmAsset)` — same.
- **L2007 / L2017:** `BuildFacetDispatcher(asset)` uses the no-context overload, so the facet mapper never writes
  `CurrentActionFqn` / `CurrentNodeVisualId` for the picker to read.

A `sharedSchemaExporter = new ActionSchemaExporter()` already exists at **L1882** — reuse it.

## The fix (all in `EditorSubsystem.cs`, the `_aiDocumentManager.ActiveChanged` handler + registrar ctors)
1. **Share one `*FacetFqnContext` per active asset between the dispatcher (writer) and drawers (reader).** In the
   `ActiveChanged` handler, for the BTree branch create a `BTreeFacetFqnContext ctx` and pass the SAME instance to
   BOTH:
   - `BTreePickerDrawerFactory.BuildDrawers(btreeAsset, _behaviorRegistry, sharedSchemaExporter, ctx)` (L2002), and
   - `BTreeSelectionBridgeHelper.BuildFacetDispatcher(btreeAsset, ctx)` (L2007 — use the existing
     `(asset, fqnContext)` overload at `BTreeSelectionBridgeHelper.cs:114`).
   Mirror for HSM: create `HsmFacetFqnContext ctx`, pass to `HsmPickerDrawerFactory.BuildDrawers(hsmAsset,
   sharedSchemaExporter, ctx)` (confirm the BuildDrawers signature at `HsmPickerDrawers.cs:482`) and to the HSM
   facet dispatcher. **`HsmSelectionBridgeHelper.BuildFacetDispatcher` currently has ONLY a no-context overload
   (`HsmSelectionBridgeHelper.cs:154`)** — add an overload `BuildFacetDispatcher(HsmAsset?, HsmFacetFqnContext?)`
   that constructs the dispatcher with the context (the `HsmFacetDispatcher` context-aware ctor exists from BB1B —
   verify and use it).
2. **Wire the accessor on both registrars (L1943, L1952):** add
   `expressionTargetFieldAccessor: <accessor>` where `<accessor>` is `Func<object?,string?>` returning the
   facet's `ExpressionTargetField` for `BTreeActionFacet`, `BTreeConditionFacet`, `TransitionFacet`,
   `GlobalTransitionFacet` (else null) — identical to the switch in
   `DefaultValueAuthoringTests.BuildAccessor`. Put it in a shared private helper (e.g.
   `EditorSubsystem.ResolveExpressionTargetField(object? facet)`) so both registrars and a test can use it.
3. Keep the "else" branch (L2019-2028) clearing drawers/dispatchers as-is; ensure no stale context leaks across
   asset switches (a fresh context per ActiveChanged is simplest).

## Tests required
- **Integration through the real bridge helpers + factory (no full subsystem needed):** build a BTree asset with
  an Action node bound to a known FQN (`DtoType=T`) and blackboard vars of T and U; create ONE
  `BTreeFacetFqnContext`; build the dispatcher via `BuildFacetDispatcher(asset, ctx)` and the drawers via
  `BuildDrawers(asset, registry, exporter, ctx)`; drive `dispatcher.GetFacet(selection)` for the action node,
  then assert the `BlackboardFieldPickerDrawer` from the drawer map returns ONLY the T vars (the shared context
  made filtering work end-to-end). Repeat for HSM (transition). This is the test that would have caught the gap.
- A test for the accessor helper: returns the bound name for a BTree Action facet + HSM transition facet, null
  otherwise (can reuse the BB1C pattern).
- **Boot:** run the `EditorSubsystemBoot` test(s) — must stay green (the subsystem still constructs).
- Full affected suites green (Stability filter): Hrot.BTree.Editor.Tests, Hrot.Hsm.Editor.Tests,
  Hrot.Editor.AiShared.Tests, Hrot.AiEditor.Persistence.Tests, plus whatever covers EditorSubsystem boot.

## Success Criteria
- [ ] EditorSubsystem passes `sharedSchemaExporter` + a shared `*FacetFqnContext` to both BuildDrawers and
      BuildFacetDispatcher (BTree + HSM), and `expressionTargetFieldAccessor` to both registrars.
- [ ] HSM `BuildFacetDispatcher(asset, ctx)` overload added.
- [ ] Integration test proves type-filtering works through the real bridge-helper + factory seam (BTree + HSM).
- [ ] Build 0/0; boot green; full affected suites 0 failed / 0 new.

## Report (`reports/BATCH-BB1D-REPORT.md`)
The exact lines changed in EditorSubsystem; how the context is shared per ActiveChanged; the accessor helper;
the integration test; boot result; exact test counts; suggested commit message. Note clearly what STILL needs a
running-editor visual check (the actual ImGui combo/panel render — REVIEW-BB1).

## Hard rules
- Grep/Glob/Read only. Do not touch `Hrot.IG`/DDS/`Stride/`; stay on `EditorSubsystem`.
- Tests verify real behavior through the real seams — the integration test MUST fail if the context isn't shared.
- Run the FULL affected suites; fix root causes; do not stop for permission.
