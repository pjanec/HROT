# BTree / HSM Visual Editing — Task Detail

Atomic task specs. Brief checklist + status lives in [TASK-TRACKER.md](./TASK-TRACKER.md).
**Read the Working Agreement** in the tracker before any batch — the anti-cheat + single-objective rules are mandatory.
**Design of record:** [docs/blueprints/BTree_HSM_Editor_State_And_Forward_Plan.md](../../docs/blueprints/BTree_HSM_Editor_State_And_Forward_Plan.md) (this is the substrate-of-record; the host docs `BTree_Editor_NodeEditor_Host_Design.md` / `HSM_Editor_NodeEditor_Host_Design.md` carry the feature/UX detail).

> All paths below are repo-relative and explicit (no guessing). Tools/projects are named with full paths.

---

## Verification & baseline

- **Build:** `dotnet build IOS-IG-SimHost.sln` — 0 errors; touched projects 0 *new* warnings.
- **Test projects (run the ones a task names):**
  - `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests`
  - `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests`
  - `Hrot/Editor/Hrot.Editor.AiShared.Tests`
  - `Hrot/Subsystems/AI/Hrot.AiEditor.Generators.Tests` (only if a task touches the generators)
- **Run WITHOUT** `BLUEPRINT_REGENERATE_SNAPSHOTS` (regen mode masks mismatches by overwriting goldens).
- **[VISUAL GATE]** tasks: lead/user confirms appearance in the running editor:
  `dotnet run --project Hrot/Runner/Hrot.ClusterRunner -- --mode editor`. Zoo does NOT run this; Zoo makes the logic headless-testable.
- **Pre-existing failures (do NOT chase; keep the failing set a subset, 0 new):** the `Hrot.Blueprints.Tests` DEBT-006 set (`ConditionSummary`, `AllocationFree`, AiPrimitive/Library goldens) and the flaky sub-150ns WhenNode perf test (DEBT-014). These are not in our test projects; if our projects show any failure, it is ours.

## Shared key files (referenced by multiple tasks)

- BTree palette: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeNodeCatalog.cs`
- BTree projection (node `Category`/`State`/`Title`, pill `Label`/`Glyph`): `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs`
- BTree host composition (where the catalog/host services are built): `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeDocumentFactory.cs`
- BTree kinds map: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeKinds.cs`
- Action source: `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IActionSchemaExporter.cs` (+ `ActionSchemaExporter.cs`); unified `IBehaviorActionCatalog` (AN3) if present in tree.
- BTree validators: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Validation/BTreeAssetValidator.cs`, `BTreeValidator.cs`
- Diagnostics window + registrar wiring: `Hrot/Editor/Hrot.Editor.AiShared/Windows/DiagnosticsWindow.cs`; `Hrot/Editor/Hrot.Editor.AiShared/Windows/PerspectiveWorkspaceRegistrar.cs`; composition root `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (BTree registrar ctor ≈ line 1904).
- HSM command sink: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmCommandSink.cs`
- HSM projection / link / pin: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmGraphModel.cs`, `HsmTransitionLink.cs`, `HsmPinModel.cs`, `HsmAsset.cs`
- HSM link validator: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Host/HsmLinkValidator.cs`
- HSM renderers: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Renderers/HsmInitialArrowRenderer.cs`, `HsmRegionConflictsRenderer.cs`
- New-asset services + asset roots (from main-toolbar-1): `Hrot/Subsystems/AI/Hrot.BTree.Editor/BTreeNewAssetService.cs`, `Hrot/Subsystems/AI/Hrot.Hsm.Editor/HsmNewAssetService.cs`, and the `AssetRoots` class (MTB-P0-T1) — **use `AssetRoots.AssetsFor(...)` / the Recipes root; do NOT hardcode `Trees/`**.

---

# Phase A — BTree

## TASK-BT-01 — Live action/condition palette
**Status:** ⚪ TODO · **Deliverable:** the node palette lists specific registered Actions/Conditions (+ Blueprint-hosted AiPrimitives), searchable; placing one bakes its method identity.
**Design ref:** Forward-Plan §5 (EB-C); host doc §5.1 (dynamic catalog).
**Scope:** Inject the existing action source (`IActionSchemaExporter` / `IBehaviorActionCatalog`) into `BTreeNodeCatalog` (today static-only — see its class comment) and thread it through `BTreeDocumentFactory.Build`. Emit one catalog entry per Action and per Condition, categorized, with keywords for fuzzy search. The generic Action/Condition entries may remain as a fallback. Re-query on `IAssetCatalog.Changed`.
**Key files:** `BTreeNodeCatalog.cs`, `BTreeDocumentFactory.cs`, `IActionSchemaExporter.cs`.
**Constraints:** Do NOT change the binding path (the Inspector `BehaviorHashPicker`/BB1 picker is owned and working). Decorators stay attach-to-node (palette action = AttachToSelected) — never free nodes. Placing an action node must set the node's method identity so the Inspector reflects it.
**Tests (headless, `Hrot.BTree.Editor.Tests`):** catalog yields ≥1 entry per exporter action; entry carries the action FQN/key; search by action name returns it; placing the entry produces a node whose bound method == the action (assert the value, not a string match). Re-query picks up a newly-added action.
**Success:** palette query returns specific actions; placement bakes identity; existing tests green.

## TASK-BT-02 — Node colors by kind
**Status:** ⚪ TODO · **Deliverable:** composites, leaves, and decorator hosts render in distinct category colors instead of one uniform color. **[VISUAL GATE]**
**Design ref:** Forward-Plan §5 (EB-B); host doc §2/§6.
**Scope:** `BTreeNodeModel.Category` is hardcoded `NodeCategory.FlowControl`. Map it by kind: composites → FlowControl, Action → Function, Condition → Pure, Wait → Function, Subtree → Macro (match the host-doc palette categories / `BTreeNodeCatalog` category paths).
**Key files:** `BTreeGraphModel.cs` (`BTreeNodeModel.Category`), `BTreeKinds.cs`.
**Tests (headless):** for each kind, `BTreeNodeModel.Category` returns the mapped value. (Pixel color is the visual gate, not Zoo's.)
**Success:** category mapping correct per kind; visual gate later confirms distinct colors.

## TASK-BT-03 — Pill glyph + param label
**Status:** ⚪ TODO · **Deliverable:** decorator pills show a glyph + value (e.g. `↺ 3`, `⏲ 2s`, Inverter glyph) instead of the bare enum name. **[VISUAL GATE]**
**Design ref:** Forward-Plan §5 (EB-B); host doc §6.
**Scope:** `BTreePillAttachmentModel` currently sets `Glyph => null`, `Label => DecoratorType.ToString()`. Provide a per-`DecoratorType` glyph and a label that includes the relevant param (`IntParam` for Repeater, `FloatParam` for Cooldown; none for Inverter/ForceSuccess/etc.).
**Key files:** `BTreeGraphModel.cs` (`BTreePillAttachmentModel`).
**Tests (headless):** Repeater(3) → label contains "3" + repeater glyph; Cooldown(2.0) → label contains "2" + cooldown glyph; Inverter → glyph, no param. Assert the actual strings/glyph keys.
**Success:** label/glyph reflect type + param; visual gate confirms readability.

## TASK-BT-04 — Validators → Diagnostics window
**Status:** ⚪ TODO · **Deliverable:** the per-perspective Diagnostics window shows real BTree validation issues (today it is empty).
**Design ref:** Forward-Plan §5 (EB-D part 1).
**Scope:** `DiagnosticsWindow` runs `IAssetValidator`s, but the BTree `PerspectiveWorkspaceRegistrar` is constructed with no `validators:` arg (EditorSubsystem ≈ line 1904) → empty. Pass a `BTreeAssetValidator` (implementing `IAssetValidator`, `SupportedKind == BTree`) into the BTree registrar. Verify `BTreeAssetValidator` produces `AssetDiagnostic`s for the standard rules (empty composite, unbound action/condition, invalid repeater/wait, unresolved subtree).
**Key files:** `EditorSubsystem.cs` (BTree registrar ctor), `PerspectiveWorkspaceRegistrar.cs` (validators param), `BTreeAssetValidator.cs`, `DiagnosticsWindow.cs`.
**Constraints:** Composition-root wiring only — do NOT alter validator *rules* (owned, tested). Do NOT touch the HSM/Blueprint registrars.
**Tests (headless, `Hrot.Editor.AiShared.Tests` + `Hrot.BTree.Editor.Tests`):** an asset with an empty Sequence and an unbound Action yields the expected diagnostic codes/severities; a valid asset yields none.
**Success:** Diagnostics window non-empty for a broken asset.

## TASK-BT-05 — Validation inline on canvas
**Status:** ⚪ TODO · **Deliverable:** invalid nodes show an error/warning outline + ⚠ on the canvas and a banner in the Inspector. **[VISUAL GATE]**
**Design ref:** Forward-Plan §5 (EB-D part 2); host doc §11.2.
**Scope:** `BTreeNodeModel.State` is hardcoded `NodeState.Normal` and `StatusTooltip => null`. Drive them from the validator's per-node diagnostics (Error → `NodeState.Error`, Warning → `NodeState.Warning`, tooltip = message). Run validation debounced after model mutation; map diagnostics keyed by `VisualId` onto the node models. Inspector banner via the existing facet/diagnostic surface.
**Key files:** `BTreeGraphModel.cs` (`BTreeNodeModel.State`/`StatusTooltip`), `BTreeValidator.cs`/`BTreeAssetValidator.cs`, BTree facet/inspector surface.
**Constraints:** Depends on TASK-BT-04's validator. Do not block the UI thread; validation is debounced.
**Tests (headless):** a node with an unbound action → its `BTreeNodeModel.State == Error` + tooltip set; fixing it → `Normal`. Assert the enum/tooltip values.
**Success:** node state reflects diagnostics; visual gate confirms outline + ⚠.

## TASK-BT-06 — Showcase `.btree.json` + Starter recipe
**Status:** ⚪ TODO · **Deliverable:** (a) a showcase tree exercising every feature; (b) a minimal Starter recipe.
**Design ref:** Forward-Plan §4/§5 (EB-A).
**Scope:** Author a showcase `.btree.json` (valid, opens + saves + rebuilds + runs) containing: ObserverSelector with a Condition guard child (→ OBSERVES badge), a Sequence/Selector, stacked decorator pills (Repeater over Cooldown), Action + Condition leaves bound to **real registered** behaviors, a Wait, and a Subtree reference. Author a **Starter** recipe = the minimal valid tree (a Root + one empty Sequence). Place both under the correct roots via the `AssetRoots`/Recipes APIs (do NOT hardcode `Trees/`); wire the Starter into the recipe list if a discovery seam exists, else document where it goes.
**Key files:** asset root via `AssetRoots`; `BTreeNewAssetService.cs` (recipe list / `AvailableRecipes`); `BTreeJsonServices` for the schema.
**Constraints:** The showcase must reference behaviors that actually exist in the registry (else validation/compile fails). Verify it round-trips (serialize→deserialize→serialize byte-stable) and that the generator emits + it registers.
**Tests (headless):** showcase deserializes; round-trip byte-stable; projects without error; references resolve. Starter recipe deserializes to the minimal valid tree.
**Success:** opening the showcase shows pills/observer/subtree; New-from-Starter yields a buildable tree.

## TASK-BT-07 *(optional)* — In-process quick reload
**Status:** ⚪ TODO (defer unless prioritized) · **Deliverable:** BTree edits hot-reload in-process (≤100 ms target) instead of via MSBuild.
**Design ref:** Forward-Plan §5 (EB-E); JSON DD §6.5 (PU-09), D12/D14.
**Scope:** Mirror Blueprint `QuickReloadService` using the shared emit core + the `[BlueprintRegistrar]` masquerade. **Large/risky — split further before assigning to Zoo.** Likely lead-handled or multi-batch.

---

# Phase B — HSM

> **Keystone:** Phase B is gated on the command-sink create-ops (HS-01…04). Until those land, the HSM canvas cannot author and the rest can't be exercised. Each is its own batch (Zoo: one objective each).

## TASK-HS-01 — Command sink: create state
**Status:** ⚪ TODO · **Deliverable:** dragging a state kind from the palette creates a real `StateNode` on the canvas.
**Design ref:** Forward-Plan §5 (EH-01); host doc §5.4/§6.3.
**Scope:** Implement `HsmCommandSink.ApplyAddNode` (currently `{ /* TODO */ }`). Create a `StateNode` from `cmd` (kind → Simple/Composite/Parallel/Final/History/DeepHistory via `HsmKinds`), assign `StableId` from the command's assigned id, position, parent (root unless dropped into a container), register it in the asset's identity maps. Dropping a child into a simple state promotes it to composite (per host doc §6.3 — implicit promotion).
**Key files:** `HsmCommandSink.cs`, `HsmAsset.cs` (state registration), `HsmKinds.cs`.
**Constraints:** Mirror the BTree command sink's structure (`Hrot.BTree.Editor/Host/BTreeCommandSink.cs`) for consistency. `MarkDirty()` after mutation. Do NOT touch the (already-real) move/reparent/region handlers.
**Tests (headless, `Hrot.Hsm.Editor.Tests`):** AddNode(Simple) → asset has the new state with correct StableId/kind/parent; lookups resolve; adding a child to a simple state makes it a container (`IsContainer == true`).
**Success:** new states appear in the model + graph projection.

## TASK-HS-02 — Command sink: delete state
**Status:** ⚪ TODO · **Deliverable:** deleting a state removes it (and cleans up dependents).
**Scope:** Implement full `HsmCommandSink.ApplyRemoveNodes` (today only removes from parent's child list). Also remove the state from identity maps; remove or orphan-flag transitions whose source/target was the deleted state; recurse into children (deleting a composite deletes its subtree) per a clear policy; keep the BB1 node-owned-variable cleanup already present elsewhere consistent.
**Key files:** `HsmCommandSink.cs`, `HsmAsset.cs`.
**Tests (headless):** delete a leaf → gone from all maps; delete a composite → its children gone; transitions referencing a deleted state are removed/handled (assert no dangling references).
**Success:** delete leaves the model consistent (no dangling transitions/ids).

## TASK-HS-03 — Command sink: draw transition
**Status:** ⚪ TODO · **Deliverable:** dragging from one state to another creates a transition (link) with sidecar metadata.
**Design ref:** host doc §7 (transitions = links via hidden pins).
**Scope:** Implement `HsmCommandSink.ApplyAddLink` (currently `{ /* TODO */ }`). Resolve source/target states from the pins (per `HsmPinModel`/`HsmTransitionLink` convention), create a `TransitionNode` with a fresh `VisualId`, default event/kind, add to source's `OutgoingTransitions`, register in identity maps. Respect `HsmLinkValidator` (no outgoing from Final, no normal transition into History).
**Key files:** `HsmCommandSink.cs`, `HsmTransitionLink.cs`, `HsmPinModel.cs`, `HsmLinkValidator.cs`, `HsmAsset.cs`.
**Tests (headless):** AddLink(stateA→stateB) → a `TransitionNode` exists with correct Source/Target/VisualId and appears in `HsmGraphModel.Links`; AddLink from a Final state is rejected by the validator (no transition created).
**Success:** transitions can be drawn and show up as links + labels.

## TASK-HS-04 — Command sink: delete transition + collapse
**Status:** ⚪ TODO · **Deliverable:** deleting a transition removes it; collapsing a composite persists.
**Scope:** Extend `ApplyRemoveLinks` to remove the `TransitionNode` from the source's outgoing list + identity maps (keep the existing BB1 `ExpressionTargetField` node-owned-var cleanup). Implement `ApplySetContainerCollapsed` (currently `{ /* TODO */ }`) to set `StateNode.IsCollapsed`.
**Key files:** `HsmCommandSink.cs`, `HsmAsset.cs`.
**Tests (headless):** RemoveLinks → transition gone from maps + source list; SetContainerCollapsed(true/false) → `StateNode.IsCollapsed` reflects it.
**Success:** transitions deletable; collapse round-trips through save/load.

## TASK-HS-05 — Initial-state arrows
**Status:** ⚪ TODO · **Deliverable:** composite states draw the `⦿→` initial-child marker. **[VISUAL GATE]**
**Design ref:** host doc §8.1.
**Scope:** Finish the explicit TODO in `HsmInitialArrowRenderer.Render` (the LCA-highlight path already works): for each composite (and each region of a parallel), draw a filled circle + arrow to the initial child (`IsInitial`). Add a hit/geometry helper testable headlessly.
**Key files:** `HsmInitialArrowRenderer.cs`.
**Tests (headless):** given a composite with an initial child, the renderer computes the expected arrow source/target geometry (assert positions); none when no initial child. (Pixels = visual gate.)
**Success:** initial markers render; geometry tests pass.

## TASK-HS-06 — Validation surfacing
**Status:** ⚪ TODO · **Deliverable:** HSM validation appears in Diagnostics, on nodes, and as region-conflict overlays. **[VISUAL GATE]**
**Design ref:** Forward-Plan §5 (EH-03); host doc §12/§15.3.
**Scope:** (a) register `HsmAssetValidator` into the HSM registrar (mirror TASK-BT-04); (b) drive `StateNode` node-state/tooltip from diagnostics; (c) **feed** `HsmRegionConflictsRenderer.SetDiagnostics(...)` after each validation run (renderer is complete but currently unfed).
**Key files:** `EditorSubsystem.cs` (HSM registrar), `HsmAssetValidator.cs`, `HsmRegionConflictsRenderer.cs`, `HsmGraphModel.cs`/`HsmAsset.cs` (state node-state).
**Tests (headless):** broken machine → expected diagnostic codes; a lane-conflict diagnostic reaches the renderer (`LastGlyphCount > 0`); node-state reflects severity.
**Success:** conflicts/diagnostics visible; renderer fed.

## TASK-HS-07 — Showcase `.hsm.json` + Starter recipe
**Status:** ⚪ TODO · **Deliverable:** showcase machine + minimal Starter recipe.
**Design ref:** Forward-Plan §4/§5 (EH-04).
**Scope:** Author a showcase `.hsm.json` with a composite state, a parallel state with ≥2 regions, transitions (with event/guard/action labels), a history and a final state, ≥2 events, and a global transition — bound to real registered actions/guards. Starter recipe = one Simple state flagged Initial. Use `AssetRoots`/Recipes APIs (no hardcoded `Machines/`).
**Key files:** asset root via `AssetRoots`; `HsmNewAssetService.cs`; `HsmJsonServices`.
**Tests (headless):** showcase deserializes; round-trip byte-stable; projects without error; references resolve. Starter deserializes to a valid one-initial-state machine.
**Success:** opening the showcase shows containers/regions/transitions/history/final; New-from-Starter yields a valid machine.

## TASK-HS-08 — Appearance / loop hardening
**Status:** ⚪ TODO · **Deliverable:** real-content rendering verified; Events-table/Globals-strip confirmed wired.
**Scope:** Verify container/transition/label/history rendering on the showcase; confirm `HsmEventsWindow` + `HsmGlobalsStrip` are registered into the HSM perspective (if not, wire them like the canvas extra-window registration in `EditorSubsystem`); end-to-end create→edit→save→reopen test on a fresh machine.
**Key files:** `EditorSubsystem.cs` (HSM perspective window registration), `HsmEventsWindow.cs`, `HsmGlobalsStrip.cs`.
**Tests (headless):** create→edit→save→reload round-trip preserves topology + layout; window registration assertions where feasible.
**Success:** HSM authoring loop holds on fresh content; events/globals surfaces present.

---

## Not a Zoo task — DEBT-BF-04 (architect design call)

HSM-state param binding for the BB1 picker needs a per-slot ("one DTO → one variable" across Entry/Exit/Activity/Timer) design decision before implementation. Tracked in [DEBT-TRACKER.md](./DEBT-TRACKER.md); it blocks `REVIEW-BB1(HSM)` but **not** the HSM authoring tasks above. Resolve via architect consult (NotebookLM) before/at the HSM visual pass.
