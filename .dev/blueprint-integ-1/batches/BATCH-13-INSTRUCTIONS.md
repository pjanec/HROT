# BATCH-13: My Blueprint panel + Blueprint Details & Variables windows (completes Phase 4)
**Tasks:** AIE-047, AIE-048   **Phase:** 4   **Est:** ~9h
**Dependencies:** BATCH-12 (Blueprint canvas binding + host services).

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/blueprint-integ-1/DESIGN.md` §5.5; `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-047, AIE-048; `docs/blueprints/NodeEdit/D6-my-blueprint-panel.md` (panel spec).
3. `.dev/blueprint-integ-1/reviews/BATCH-12-REVIEW.md`.
4. **Templates:** NodeEdit `MyBlueprintPanel` (`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Panels/MyBlueprintPanel.cs`) + `IMyBlueprintModel` + `FakeMyBlueprintModel` (`NodeEditor.Demo/FakeBlueprint/`).

Use **codebase-memory MCP** first; not `search_code`. **Do NOT change CycloneDDS versions** (GizmoMap.Contracts stays 0.2.2). Headless tests must not call ImGui without a context.

## Ground truth
- `IMyBlueprintModel` (NodeEditor.Core): `Sections` (`MyBlueprintSectionDescriptor`), `GetItems(sectionId)` → `MyBlueprintItem`, `Changed` event. Fixed section order (see D6): Graphs, Functions, Macros, Custom Events, Variables, Event Dispatchers.
- `BlueprintAsset` fields available: `Graphs`, `Variables`, `CustomEvents`, `EventDispatchers`, `Parameters`, `WorkingState`, `CallablePeers`. (No Functions/Macros fields — those sections are faked/empty for v1.)
- Existing Blueprint windows: `Hrot.Blueprints.Editor/Variables/BlueprintVariablesWindow.cs`, node drawers + `Inspector/DrawerRegistry.cs` (Blueprint node-drawer property UI). `PerspectiveWorkspaceRegistrar.RegisterExtraWindow` seam (used in BATCH-12 for the canvas). `AiDocumentManager.ActiveChanged` to retarget panels.

## Tasks (in order)

### Task 1: BlueprintMyBlueprintModel + register MyBlueprintPanel (AIE-047) — files: `.../Windows/BlueprintMyBlueprintModel.cs` (NEW) + perspective registration
`IMyBlueprintModel` projecting the active `BlueprintAsset`: **real** sections for **Variables** (name/type/category/accent), **Graphs**, **Custom Events**, **Event Dispatchers**; **faked/empty** Functions + Macros (return empty item lists, sections still listed in fixed order). Fire `Changed` on asset mutation. Register NodeEdit `MyBlueprintPanel` (bound to this model) in the **Blueprint** `PerspectiveWorkspaceRegistrar`; retarget to the active asset via `AiDocumentManager.ActiveChanged`. Template: `FakeMyBlueprintModel`.
**Tests (`Hrot.Blueprints.Tests`):** `MyBlueprintModel_Sections_FixedOrder` (Graphs, Functions, Macros, Custom Events, Variables, Event Dispatchers); `MyBlueprintModel_Variables_ProjectAssetVariables` (assert name/type per variable); `MyBlueprintModel_Graphs_ProjectAssetGraphs`; `MyBlueprintModel_CustomEvents_AndDispatchers_Projected`; `MyBlueprintModel_FakedSections_ReturnEmpty_NoThrow`; `MyBlueprintModel_FiresChanged_OnAssetMutation`.

### Task 2: Blueprint Details + Variables windows (AIE-048) — composition + windows
Register in the **Blueprint** perspective: (a) a **Details** window that renders the selected Blueprint node's drawer (via the existing node drawers / `DrawerRegistry`), bound to the perspective's selection store; (b) the existing `BlueprintVariablesWindow`, bound to the active asset. Both retarget on active-asset/selection change. Keep them headless-testable (extract interaction/projection logic from ImGui).
**Tests:** `BlueprintDetails_SelectedNode_ResolvesDrawer` (a When/Montage/EQS node selection resolves to its drawer/session — assert the resolved drawer kind, not non-null); `VariablesWindow_ListsActiveAssetVariables` (+ edit path if the window supports it). Reuse existing `BlueprintVariablesWindow` tests where present.

## Success Criteria
- [ ] AIE-047/048 per success conditions; **Phase 4 / M-Blueprint complete** (Blueprint opens on canvas with My Blueprint outliner + node-drawer Details + Variables, structural editing from BATCH-11/12).
- [ ] `dotnet build IOS-IG-SimHost.sln` 0 errors (GizmoMap.Contracts on 0.2.2).
- [ ] Green: `Hrot.Blueprints.Tests` (no new failures beyond DEBT-006's 10), `Hrot.Editor.AiShared.Tests`, `EditorSubsystemBoot` filter.
- [ ] No warnings; docs; no leftover TODO/debug.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-13-REPORT.md`.

## Execution rules
- Tasks in sequence; run suites yourself; fix root causes; never fake a pass; assert real projected values (section order, variable name/type, resolved drawer kind), not non-null.
- Reuse the existing `MyBlueprintPanel`, `BlueprintVariablesWindow`, node drawers + `DrawerRegistry` — do NOT duplicate. Verify `IMyBlueprintModel`/`MyBlueprintItem` shape against the code.

## Report Requirements
In `reports/BATCH-13-REPORT.md`: which My Blueprint sections are real vs faked + why; how the Details window resolves node drawers; the retarget wiring; actual test counts; full-solution build 0 errors + Blueprints no new failures; suggested commit message. No comprehension questions.
