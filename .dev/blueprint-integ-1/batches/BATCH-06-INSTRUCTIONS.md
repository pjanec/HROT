# BATCH-06: BTree links (corrective) + Inspector facet dispatch + pickers + HSM globals
**Tasks:** Corrective Task 0 (BTree links), AIE-023, AIE-024, AIE-027   **Phase:** 2   **Est:** ~14h
**Dependencies:** BATCH-05 (canvas + host binding), BATCH-04 (composition root).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/blueprint-integ-1/reviews/BATCH-05-REVIEW.md` — the **P1 / Corrective Task 0** details.
3. `.dev/blueprint-integ-1/DESIGN.md` §5.4; `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-023, AIE-024, AIE-027 — success conditions.

Use **codebase-memory MCP** first (project `D-Work-IOS-IG-SimHost-FDP-2`); not `search_code`. Headless tests must not call ImGui without a context (`ImGui.GetCurrentContext()==IntPtr.Zero` guard / seams).

## Corrective Task 0 — BTreeGraphModel link projection (P1 from BATCH-05)
**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs`. Currently `Links => Array.Empty<ILinkModel>()` ⇒ BTree tree edges do not render. Project each parent↔child edge from `BehaviorTreeAsset` `ChildVisualIds` as an `ILinkModel` connecting **child.`OutputPinId` → parent.`InputPinId`** (reversed-pin convention already in the pin models). Implement `FindLink(LinkId)`. Rebuild the link cache when the asset `Changed` fires. Mirror how `HsmGraphModel` builds its `_linkCache` from transitions.
**Tests (fix the verifies-nothing test):** rewrite `BTreeDocumentFactoryTests.BTreeDocumentFactory_Build_GraphView_ExposesProjectedLinks` (and/or a new `BTreeGraphModelTests`) to build a known tree (e.g. Root→Sequence→{Action,Action}) and assert: **exact link count**, and for each link the **From pin == child.OutputPinId** and **To pin == parent.InputPinId**. No `NotBeNull`-only assertions.

## Task 1: Inspector facet dispatch (AIE-023) — files: `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs` (UPDATE) + composition dispatch in `EditorSubsystem.cs`
Wire the per-perspective `InspectorWindow` to read its perspective's `EditorSelectionStore.ActiveSubSelection`, route through `BTreeFacetMapper`/`HsmFacetMapper` (existing, in the editor assemblies) to a StructEdit facet struct, render it, and on commit apply back to the model (mark dirty). Fall back to asset-level properties when no sub-selection. The mappers depend on the active asset — instantiate per active asset (mirror the design-talk dispatch). Keep dependency direction legal (subsystem-specific mappers wired in the composition root, not in AiShared).
**Tests required:** `Inspector_BTreeNodeSelection_YieldsActionFacet` (+ Wait/Sequence); `Inspector_HsmStateSelection_YieldsStateFacet` (+ transition/region/event); `Inspector_Commit_AppliesToAsset_AndMarksDirty` (edit a facet field → mapper apply called → `asset.IsDirty==true`); `Inspector_NoSubSelection_FallsBackToAssetProperties`.

## Task 2: Custom StructEdit field pickers + PickerRegistry.Get fix (AIE-024 + DEBT-003) — files: existing BTree/HSM field-drawer classes + composition registration; `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerWindow.cs` (or wherever `PickerRegistry.Get<TItem>` lives)
Register `IImGuiFieldDrawer`s for `[BehaviorHashPicker]`, `[BlackboardFieldPicker]` (BTree) and `[HsmActionPicker]`, `[HsmGuardPicker]`, `[HsmStateSelector]`, `[HsmEventPicker]`, `[HsmSyncGroupPicker]` (HSM) with the inspector's StructEdit service builder (composite drawer for shared CLR types like `string`/`ushort`). **DEBT-003:** `PickerRegistry.Get<TItem>` currently returns `null` (unfinished) — implement it to return the registered source for a type (it's needed by the canvas wire-drop/inspector pickers); add a test.
**Tests required:** `FieldPicker_BehaviorHash_ListsRegistryNames`; `FieldPicker_BlackboardField_ListsActiveAssetFields`; `FieldPicker_HsmEvent_ListsAssetEvents`; `FieldPicker_HsmState_ListsAssetStates`; `CompositeStringDrawer_DispatchesByAttribute` (unmarked string falls through to default); `PickerRegistry_Get_ReturnsRegisteredSource` (+ null for unregistered).

## Task 3: HsmGlobalsStrip (AIE-027) — file: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Windows/HsmGlobalsStrip.cs` (UPDATE stub) + register in HSM perspective
Finish the stub: render a chip per `HsmAsset.AllGlobalTransitions` (event→target), click → set `HsmGlobalTransitionSelection` sub-selection on the HSM selection store, context menu (edit/change-target/remove → dispatch through the HSM command sink). Register the strip in the HSM `PerspectiveWorkspaceRegistrar` via the extension seam. Keep ImGui headless-safe; extract interaction logic for testing.
**Tests required:** `HsmGlobalsStrip_RendersChipPerGlobalTransition`; `HsmGlobalsStrip_ClickChip_SetsGlobalTransitionSubSelection`; `HsmGlobalsStrip_Remove_DispatchesCommand` (logic-level over fakes).

## Success Criteria
- [ ] Corrective Task 0 + AIE-023, AIE-024, AIE-027 per success conditions.
- [ ] Green (full, no crashes): `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `NodeEditor.UI.Tests`, `EditorSubsystemBoot` filter. `Hrot.Blueprints.Tests` no new failures beyond DEBT-006's 10. (Full `Hrot.ClusterRunner.Integration.Tests` has a pre-existing SimHost abort — DEBT-008; run the `EditorSubsystemBoot` filter instead.)
- [ ] No warnings; docs; no leftover TODO/debug.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-06-REPORT.md`.

## Execution rules
- Corrective Task 0 **first**; prove BTree links project with the strengthened test before moving on.
- Tasks in sequence; run the named suites yourself; fix root causes; never fake a pass or assert-NotNull-only. Verify mapper/picker/attribute names against the existing code (don't invent).

## Report Requirements
In `reports/BATCH-06-REPORT.md`: how BTree links now project (+ the strengthened test); how facet dispatch is wired without breaking AiShared→subsystem dependency direction; the picker registration approach + PickerRegistry.Get fix; exact test counts (all named suites); confirm the `EditorSubsystemBoot` filter stays 10/10; suggested commit message. No comprehension questions.
