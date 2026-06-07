# BATCH-02: Shared backing — unified catalog, Blueprint contributor, document manager
**Tasks:** AIE-010, AIE-011, AIE-012   **Phase:** 1   **Est:** ~11h
**Dependencies:** BATCH-01 (adapters exist; not directly used here).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/blueprint-integ-1/DESIGN.md` §3.1, §4.3, §4.5 — shared backing + document manager + asset sources.
3. `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-010, AIE-011, AIE-012 — authoritative success conditions.
4. `.dev/blueprint-integ-1/reviews/BATCH-01-REVIEW.md` — context (no fixes required).

Use the **codebase-memory MCP** first (project `D-Work-IOS-IG-SimHost-FDP-2`); not `search_code`.

**Scope guard:** This batch builds **standalone, unit-testable** components only. Do **NOT** edit `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (the composition-root rewrite is AIE-015 / BATCH-03). Keep everything decoupled via injected abstractions so it tests headlessly.

## Ground truth — key files (verified)
- Shared catalog: `Hrot/Editor/Hrot.Editor.AiShared/Catalog/AssetCatalog.cs` (+ `IAssetCatalogContributor`, `IEditableAsset`, `AssetKind`).
- Existing contributors: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Catalog/BTreeAssetContributor.cs` (ctor takes optional `BTreeDebugSession`; has `LoadFrom(assembly)`), `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Catalog/HsmAssetContributor.cs` (`LoadFrom(assembly)`).
- Blueprint asset + services: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/BlueprintAsset.cs` (`AssetId`, `Name`, `Dispatch`, `Graphs`, …); `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/FileSystemAssetCatalog.cs` (legacy `IAssetCatalog` — see scope note), `BlueprintJsonServices` (header deserialize) in `Hrot.Blueprints.Core`.
- Hot-reload coordinator (for reference only; do not wire here): `Fdp.Toolkit.Behavior.AiHotReloadCoordinator` / the editor's `_aiCoordinator` with `OnReloadCompleted`.
- Tests: `Hrot/Editor/Hrot.Editor.AiShared.Tests/`, `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/`. There are `BlueprintAssetBuilder` test builders in `Hrot.Blueprints.Tests/Builders/` for constructing assets.

## Tasks (do in order)

### Task 1: BlueprintAssetContributor (AIE-011) — file: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Catalog/BlueprintAssetContributor.cs` (NEW)
Implement `IAssetCatalogContributor` for `AssetKind.Blueprint`: enumerate `*.bp.json` under a root dir, header-only (lazy) — extract `AssetId`/`Name` via `BlueprintJsonServices` (or `JsonDocument`), wrap each as an `IEditableAsset` (reuse/extend `BlueprintEditableAssetAdapter` if suitable). Fire `ContributorChanged` on `Refresh()`.
**Scope note:** Do not delete `FileSystemAssetCatalog` yet (other code/tests may reference it). If it implements the legacy Blueprint `IAssetCatalog`, leave it; this new class is the **shared-catalog** contributor. Removal of the legacy path from the composition root is AIE-015. Ensure all existing `Hrot.Blueprints.Tests` still pass.
**Tests required (`Hrot.Blueprints.Tests`):** `BlueprintAssetContributor_Enumerate_FindsBpJson` (temp dir with ≥2 `.bp.json` → one `IEditableAsset` each, correct `AssetId`/`Name`, header-only); `BlueprintAssetContributor_FiresChanged_OnRefresh`; `BlueprintAssetContributor_IgnoresMalformedJson` (skips, no throw).

### Task 2: Unified AssetCatalog aggregation + assembly refresh (AIE-010) — file: `Hrot/Editor/Hrot.Editor.AiShared/Catalog/AiAssetCatalogBuilder.cs` (NEW; small composable helper)
Provide a helper that builds an `AssetCatalog` from the three contributors (BTree, HSM, Blueprint) and exposes `RefreshFromAssembly(Assembly aiAssembly)` which calls `LoadFrom(asm)` on the BTree+HSM contributors (and `Refresh()` on the Blueprint contributor) — to be invoked at init and on each hot reload. Do **not** wire it into `EditorSubsystem` (that's AIE-015); just make it a constructible, testable unit. Confirm `AssetCatalog.Changed` fires when contributors change.
**Tests required (`Hrot.Editor.AiShared.Tests`):** `AssetCatalog_AfterLoadFrom_ListsBTreeAndHsmAssets` — feed an assembly containing `[BTreeDefinition]`/`[HsmDefinition]` methods (use the loaded `Hrot.AI.Behaviors.dll` via `Assembly.Load`, or a purpose-built fake assembly/types) and assert the catalog enumerates the expected entries by kind; `AiAssetCatalogBuilder_Refresh_RaisesCatalogChanged` (simulate reload → `Changed` fires and the merged list rebuilds); `AssetCatalog_MergesAllThreeKinds` (BTree+HSM+Blueprint contributors → entries of all three kinds present). Existing `AssetCatalogTests` must still pass.

### Task 3: AiDocumentManager (AIE-012) — file: `Hrot/Editor/Hrot.Editor.AiShared/Documents/AiDocumentManager.cs` (NEW)
Owns open documents (`AiDocument { IEditableAsset Asset, AssetKind Kind, object? ViewState, bool IsDirty }`) and the active document. API: `Open(asset)` (focus if already open, else add + activate), `Activate(doc)`, `Close(doc)` (activate next or none). Activating must (a) set `Active`, (b) invoke an injected perspective-switch abstraction with the asset's kind, (c) raise an `ActiveChanged` event so panels retarget. **Decouple from WindowManager/GraphView:** inject an `Action<string>`/`IPerspectiveSwitcher` for the perspective switch and a focus callback; the manager must NOT construct GraphViews or call ImGui. `ViewState` is an opaque slot the canvas (Phase 2) fills and the manager preserves across activations.
**Tests required (`Hrot.Editor.AiShared.Tests`):** `AiDocumentManager_Open_AddsDocument_AndActivates`; `_OpenAlreadyOpen_FocusesExisting_NoDuplicate`; `_Activate_InvokesPerspectiveSwitchWithKind` (BTree asset → switch called with `"BTree"`); `_Close_RemovesDocument_AndActivatesNextOrNone`; `_PreservesViewStatePerDocument` (set ViewState on doc A, switch to B and back → same ViewState object/instance preserved); `_ActiveChanged_FiresOnActivate`.

## Success Criteria
- [ ] AIE-010, AIE-011, AIE-012 implemented per TASK-DETAIL success conditions.
- [ ] New tests pass **and** full suites green for `Hrot.Editor.AiShared.Tests` and `Hrot.Blueprints.Tests` (and any AI editor test project you touch).
- [ ] `EditorSubsystem.cs` is **not** modified in this batch.
- [ ] No warnings; public APIs documented; no leftover TODO/debug code.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-02-REPORT.md`.

## Execution rules
- Complete tasks **in sequence**; do NOT start the next until the current task's impl + tests are done and ALL tests (incl. prior batches') pass.
- Run the relevant `dotnet test` suites yourself; fix root causes to completion; never swallow errors or fake a pass. Tests must assert real values/behavior.

## Report Requirements
Answer in `reports/BATCH-02-REPORT.md`: issues & fixes; weak points; design decisions beyond spec (e.g. the perspective-switch abstraction shape, IEditableAsset wrapper choice); edge cases; whether you used the real `Hrot.AI.Behaviors.dll` or a fake assembly for AIE-010 and why; actual test counts; suggested commit message. No comprehension questions.
