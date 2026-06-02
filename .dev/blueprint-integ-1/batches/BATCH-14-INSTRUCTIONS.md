# BATCH-14: Phase 5 wiring — comparison sanitizers, reference contributors/refactor, blackboard aggregation
**Tasks:** AIE-050, AIE-051, AIE-052   **Phase:** 5   **Est:** ~10h
**Dependencies:** AIE-010 (comparison/reference infra), AIE-025 (blackboard authoring window). All target services already exist — this batch is composition-root **wiring** + minimal window consumption.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`.
2. `.dev/blueprint-integ-1/DESIGN.md` §5.7; `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-050, AIE-051, AIE-052; `.dev/blueprint-integ-1/design-talk.md` Steps 7, 8, 11.
3. `.dev/blueprint-integ-1/reviews/BATCH-13-REVIEW.md`.

Use **codebase-memory MCP** first; not `search_code`. **Do NOT change CycloneDDS versions** (GizmoMap.Contracts stays 0.2.2); do not touch Hrot.IG/DDS. Headless tests must not call ImGui without a context.

## Ground truth (verify before coding — these are the seams)
- Composition root: `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` → `RegisterWindows()` (~line 1544). It already builds `catalog`, `referenceCatalog = new ReferenceCatalog(catalog)` (~1553), `refactorService = new RefactorService(referenceCatalog, catalog, new AtomicMultiFileWriter())` (~1554), and a `FindResultsWindow` for the asset browser (~1631). Wire the new pieces here. **The editor composes services manually with `new` — it does NOT use an `IServiceCollection`.**
- **AIE-050 comparison:** `Hrot.Editor.AiShared/Comparison/SanitizerRegistry.cs` + `ComparisonExportBuilder.cs`. The per-kind registration helpers are **DI extensions** (`AddBTreeEditorComparison`/`AddHsmEditorComparison`/`AddBlueprintEditorComparison` in each subsystem's `Comparison/*ServiceCollectionExtensions.cs`) that do `sp.GetRequiredService<SanitizerRegistry>().Register(sanitizer)`. Since the editor has no `IServiceCollection`, **construct a `SanitizerRegistry` and register each kind's sanitizer directly** (read the extension bodies to learn the concrete sanitizer ctors). Then construct `ComparisonExportBuilder` against it. Confirm the registry/builder ctor shapes against the code.
- **AIE-051 references:** `ReferenceCatalog(IAssetCatalog?, IEnumerable<IReferenceCatalogContributor>?)` — contributors are a **ctor arg, currently omitted**. Pass the real contributors: `BTreeBlackboardVariableContributor` (`Hrot.BTree.Editor/Catalog/`), plus the HSM and Blueprint reference contributors (find them via the graph — verify exact type names + ctor deps). `RefactorService` + `FindResultsWindow` are already wired; just ensure they now see references because the catalog has contributors.
- **AIE-052 aggregation:** `Hrot.Editor.AiShared/Blackboard/IBlackboardAggregator.cs` → `BlackboardAggregatorService(IEnumerable<IBlackboardAggregatorStrategy>, IActionSchemaExporter, IAssetCatalog)` + `internal Register(strategy)`. Strategies exist: `BTreeBlackboardAggregatorStrategy` (`Hrot.BTree.Editor/Blackboard/`), `HsmBlackboardAggregatorStrategy` (`Hrot.Hsm.Editor/Blackboard/`). **`BlackboardAuthoringWindow` does NOT consume the aggregator today** — wire the service into composition AND feed the window's bin-packing (add a minimal ctor param / setter on `BlackboardAuthoringWindow` to accept an aggregation source; verify the window's current bin-packing input first). Find the `IActionSchemaExporter` impl already used in the editor.

## Tasks (in order)

### Task 1: Reference catalog contributors + refactor/find wiring (AIE-051)
Pass the BTree/HSM/Blueprint `IReferenceCatalogContributor`s into the `ReferenceCatalog` ctor in `EditorSubsystem`. Confirm `RefactorService`/`FindResultsWindow` resolve references across assets via the populated catalog.
**Tests (`Hrot.Editor.AiShared.Tests` and/or subsystem test projects):** `ReferenceCatalog_FindReferences_AcrossAssets` (a reference authored in one asset to a target in another is found — assert host/target ids); `RefactorService_Rename_WritesAtomically` (rename writes all edits atomically; assert written file set + content). Reuse existing `ReferenceCatalogTests`/`RefactorServiceTests` — they must still pass.

### Task 2: Blackboard aggregator strategies (AIE-052)
Construct `BlackboardAggregatorService` with `[BTreeBlackboardAggregatorStrategy, HsmBlackboardAggregatorStrategy]` (+ the editor's `IActionSchemaExporter` + catalog) in `EditorSubsystem`; feed its `Aggregate` output into `BlackboardAuthoringWindow`'s bin-packing so budget warnings surface.
**Tests:** `Aggregator_BTree_CollectsSubtreeRequirements` (a BTree with an action node yields its DTO requirement with provenance); `Aggregator_Hsm_CollectsStateActionRequirements`; a bin-packer test asserting a budget-overflow surfaces an `AggregationWarning`/budget warning. Reuse `BlackboardAggregatorServiceTests`.

### Task 3: Comparison sanitizers + ComparisonExportBuilder (AIE-050)
Construct a `SanitizerRegistry`, register the BTree/HSM/Blueprint (+ Blackboard/Utility if present) sanitizers directly (mirroring the DI-extension bodies), and construct `ComparisonExportBuilder` against it in `EditorSubsystem`. Expose/use it wherever the comparison flow is consumed (verify whether a comparison window already exists; if so, feed it the registry/builder — do NOT invent a new window if one exists).
**Tests:** `SanitizerRegistry_HasSanitizer_PerAssetKind` (registry returns a sanitizer for BTree/HSM/Blueprint); a determinism test — sanitizing two versions of one asset produces identical stripped output for unchanged content. Reuse `SanitizerRegistryTests`/`ComparisonExportBuilderTests`.

## Success Criteria
- [ ] AIE-050/051/052 per success conditions.
- [ ] `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 warnings (GizmoMap.Contracts on 0.2.2).
- [ ] Green: `Hrot.Editor.AiShared.Tests`, `Hrot.Blueprints.Tests` (no new failures beyond DEBT-006's 10), `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, and `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot`.
- [ ] No leftover TODO/debug; docs.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-14-REPORT.md`.

## Execution rules
- Tasks in sequence (references → aggregation → comparison). Run the suites yourself; fix root causes; never fake a pass; assert real values (found reference ids, atomic written-file set, DTO requirement provenance, per-kind sanitizer presence, deterministic stripped output), not non-null.
- **Reuse existing services** (`ReferenceCatalog`/`RefactorService`/`BlackboardAggregatorService`/`SanitizerRegistry`/`ComparisonExportBuilder` and the existing contributors/strategies/sanitizers). Do NOT reimplement them. Verify every ctor/type name against the code before use.
- If the comparison/aggregator consumption point requires a new ctor param on an existing window, keep it additive and minimal; don't rewrite the window.

## Report Requirements
In `reports/BATCH-14-REPORT.md`: the exact contributor/strategy/sanitizer types you registered + their ctor deps; how you reconciled the DI-extension comparison helpers with the editor's manual composition; how the aggregator feeds the authoring window; actual test counts; full-solution build 0 errors/0 warnings + no new Blueprints failures; suggested commit message. No comprehension questions.
