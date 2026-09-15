# BATCH-14 Report

## Implementation Summary

**MTB-P5-T1:** Typed `IAssetCatalog.Changed` from `event Action?` to `event Action<AssetKind>?`, wired `AssetCatalog` to pass each contributor's `Kind` through the event, added a Scenario early-return guard in `ReferenceCatalog.OnCatalogChanged`, updated all subscribers and 15+ IAssetCatalog fake implementations across 5 test projects.

### Task 1 — Typed event
- `IAssetCatalog.Changed`: `event Action?` → `event Action<AssetKind>?` — carries the `AssetKind` that changed.
- `AssetCatalog.AddContributor`: wires `contributor.ContributorChanged += () => OnContributorChanged(contributor.Kind)`.
- `AssetCatalog.OnContributorChanged(AssetKind kind)`: rebuilds cache (unchanged) then `Changed?.Invoke(kind)`.

### Task 2 — ReferenceCatalog skips Scenario (§10.4)
- `ReferenceCatalog.OnCatalogChanged(AssetKind kind)`: **early-returns** when `kind == AssetKind.Scenario` — no `_elements.Clear()`, no `_references.Clear()`, no contributor walk, and **no `Changed?.Invoke()`**.
- For all other kinds, the existing clear+rebuild+`Changed.Invoke()` path runs exactly as before.
- `IReferenceCatalog.Changed` is UNCHANGED (`event Action?`).

### Task 3 — Remaining subscribers (behavior preserved)
- `ActionSchemaExporterCatalogWatcher.OnCatalogChanged(AssetKind kind)` — accepts the arg, keeps calling `_exporter.Rebuild()` unconditionally.
- `AssetBrowserPanel.OnCatalogChanged(AssetKind kind)` — accepts the arg, keeps calling `RebuildTrees()` unconditionally (panel shows all kinds).
- All test lambdas: `catalog.Changed += () => count++` → `catalog.Changed += _ => count++`.

### Tests added (ReferenceCatalogTests)
- **`ScenarioChange_DoesNotRebuild_References`** — populates via non-scenario change, then fires `Changed(AssetKind.Scenario)`. Uses a `RecordingContributor` (records `EnumerateElementsCallCount`/`EnumerateReferencesCallCount`). Asserts: elements unchanged, `ReferenceCatalog.Changed` did NOT fire, contributor walk did NOT happen (call counts unchanged).
- **`NonScenarioChange_Rebuilds`** — fires `Changed(AssetKind.Blueprint)`. Asserts: elements and references reflect the catalog, `ReferenceCatalog.Changed` fired once.

## Design Decisions

1. **Default parameter for test fakes**: `FireChanged(AssetKind kind = AssetKind.Blueprint)` and `RaiseChanged(AssetKind kind = AssetKind.Blueprint)` — backwards-compatible with existing callers that pass no arg, while enabling the new tests to pass `AssetKind.Scenario` explicitly.

2. **RecordingContributor**: added as a test-only `IReferenceCatalogContributor` that tracks call counts, used to prove the Scenario skip avoids the contributor walk entirely.

3. **`IReferenceCatalog.Changed` kept as `Action?`**: this is a separate interface and its event contract is unchanged per spec.

## Deviations

None. All changes match the batch instructions exactly.

## Test Results

### New tests (unfiltered)
```
dotnet test Hrot.Editor.AiShared.Tests --filter "FullyQualifiedName~ReferenceCatalogTests"
  Passed! - Failed: 0, Passed: 14, Skipped: 0, Total: 14, Duration: 26 ms
```
All 14 ReferenceCatalogTests pass (12 existing + 2 new). New tests pass **unfiltered**.

### Required suites (Stability filter — 0 failed)
```
dotnet test Hrot.Editor.AiShared.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
  Passed! - Failed: 0, Passed: 925, Skipped: 0, Total: 925, Duration: 4 s

dotnet test Fdp.Toolkits.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
  Passed! - Failed: 0, Passed: 1856, Skipped: 0, Total: 1856, Duration: 23 s

dotnet test Hrot.SimHost.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
  Passed! - Failed: 0, Passed: 585, Skipped: 3, Total: 588, Duration: 12 s
  (StagingEntityExtractorTests flake on first run; passed on re-run — pre-existing)
```

### Indirectly touched suites (Stability filter — 0 failed)
```
dotnet test Hrot.BTree.Editor.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
  Passed! - Failed: 0, Passed: 399, Skipped: 0, Total: 399

dotnet test Hrot.Hsm.Editor.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
  Passed! - Failed: 0, Passed: 352, Skipped: 0, Total: 352

dotnet test Hrot.Blueprints.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
  Failed! - Failed: 9, Passed: 1843, Skipped: 8, Total: 1860
  (All 9 failures pre-existing: AiPrimitiveEmitGoldenTests, Stage8Tests, AllocationFreeTests,
   MoveToAndFireDemoTests, CF2/CF7 debug end-to-end, WhenNodePerfTests — unrelated to catalog changes)
```

### Directly touched test classes (unfiltered — 0 failed)
```
dotnet test Hrot.Editor.AiShared.Tests --filter "FullyQualifiedName~ReferenceCatalogTests|FullyQualifiedName~AssetCatalogTests|FullyQualifiedName~AiAssetCatalogBuilderTests|FullyQualifiedName~ActionSchemaExporterTests|FullyQualifiedName~AssetBrowserPanelTests"
  Passed! - Failed: 0, Passed: 79, Skipped: 0, Total: 79, Duration: 4 s
```

### Build
```
dotnet build IOS-IG-SimHost.sln
  Build succeeded. 0 Warning(s), 0 Error(s)
```

NO `BLUEPRINT_REGENERATE_SNAPSHOTS` was set. No tests were skipped, weakened, or auto-passed.

## Developer Insights

### Issues encountered
1. **Two `IAssetCatalog` interfaces exist**: the AiShared one (`Hrot.Editor.AiShared.Catalog.IAssetCatalog` — has `Changed`) and the Blueprints one (`Hrot.Blueprints.Editor.IAssetCatalog` — has only `EnumerateAll()`, no `Changed`). The Blueprints one is a completely different contract with different members. Only the AiShared one was modified. `FileSystemAssetCatalog` and `EmptyCatalog`/`StubCatalog`/`StubAssetCatalog` in Blueprints.Tests implement the Blueprints one and were unaffected.

2. **sed overreach**: initial batch replacement of `event Action? Changed` → `event Action<AssetKind>? Changed` accidentally hit `FakeAsset : IEditableAsset` classes and `StubSchemaExporter : IActionSchemaExporter` classes. These were manually reverted. The per-file approach with targeted `sed` followed by manual review of each hit worked well after the initial mistake.

3. **15+ IAssetCatalog fake/stub implementations** across 5 test projects needed the signature change. All were test-only fakes that either never fire `Changed` or use a no-arg helper with a default parameter.

### Weak points / improvement opportunities
- The two `IAssetCatalog` interfaces with the same name but different contracts is confusing. Consider renaming the Blueprints one to `IBlueprintAssetCatalog` to disambiguate.
- Many test fakes duplicate `IAssetCatalog` stubs across projects. A shared test helper library could reduce this.

### Edge cases discovered
- `IEditableAsset.Changed` is also `Action?` — distinct from `IAssetCatalog.Changed`. Care must be taken not to conflate them.
- The `ActionSchemaExporterCatalogWatcher.Dispose()` correctly unsubscribes with the new delegate type without changes.

## Known Issues

- 9 pre-existing test failures in `Hrot.Blueprints.Tests` (unrelated to catalog changes: golden emit, PDB, allocation, debugger, performance benchmarks). These were present before BATCH-14 and are not addressed here.
- `StagingEntityExtractorTests.Extract_WithEpisodeId_AppendsEpisodeTagToComponents` in SimHost.Tests is intermittently flaky (passes in isolation, sometimes fails in full suite). Pre-existing; not addressed.

## Files Changed (20 total)

### Production code (5)
| File | Change |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalog.cs` | `event Action? Changed` → `event Action<AssetKind>? Changed` |
| `Hrot/Editor/Hrot.Editor.AiShared/Catalog/AssetCatalog.cs` | Typed event + wire `contributor.Kind` to `OnContributorChanged(kind)` |
| `Hrot/Editor/Hrot.Editor.AiShared/References/ReferenceCatalog.cs` | `OnCatalogChanged(AssetKind kind)` with Scenario early-return |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/ActionSchemaExporterCatalogWatcher.cs` | `OnCatalogChanged(AssetKind kind)` signature update |
| `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetBrowserPanel.cs` | `OnCatalogChanged(AssetKind kind)` signature update |

### Test code (15)
| File | Change |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/References/ReferenceCatalogTests.cs` | New tests + RecordingContributor + FakeAssetCatalog update |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Catalog/AssetCatalogTests.cs` | Lambda `() =>` → `_ =>` |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Catalog/AiAssetCatalogBuilderTests.cs` | Lambda `() =>` → `_ =>` |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/ActionSchemaExporterTests.cs` | FakeCatalog.Changed + RaiseChanged signature |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardAggregatorServiceTests.cs` | StubCatalog.Changed signature |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/AssetBrowserPanelTests.cs` | FakeCatalog.Changed + RaiseChanged signature |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Comparison/Batch14SanitizerRegistryTests.cs` | EmptyCatalog.Changed signature |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Refactor/RefactorServiceTests.cs` | FakeAssetCatalog.Changed signature |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Comparison/FakeCatalogHelper.cs` | FakeCatalog.Changed signature (FakeAsset unchanged) |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeSubtreeResolverTests.cs` | FakeCatalog.Changed signature |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Blackboard/BTreeBlackboardAggregatorTests.cs` | StubCatalog.Changed signature (StubSchemaExporter unchanged) |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Catalog/ReferenceCatalogCrossAssetTests.cs` | TestCatalog.Changed + FireChanged signature |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Comparison/FakeCatalogHelper.cs` | FakeCatalog.Changed signature (FakeAsset unchanged) |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Blackboard/HsmBlackboardAggregatorTests.cs` | StubCatalog.Changed signature (StubSchemaExporter unchanged) |
| `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Comparison/BlueprintComparisonSanitizerTests.cs` | FakeCatalog.Changed signature (FakeAsset unchanged) |

### Full subscriber list (IAssetCatalog.Changed)
| Subscriber | Location | Action |
|------------|----------|--------|
| `ReferenceCatalog.OnCatalogChanged` | `Hrot.Editor.AiShared/References/ReferenceCatalog.cs` | Early-return on Scenario; rebuild on others |
| `ActionSchemaExporterCatalogWatcher.OnCatalogChanged` | `Hrot.Editor.AiShared/Blackboard/ActionSchemaExporterCatalogWatcher.cs` | Accepts arg, calls Rebuild() |
| `AssetBrowserPanel.OnCatalogChanged` | `Hrot.Editor.AiShared/Browser/AssetBrowserPanel.cs` | Accepts arg, calls RebuildTrees() |
| Test lambdas (AssetCatalogTests, AiAssetCatalogBuilderTests) | `Hrot.Editor.AiShared.Tests/` | `() =>` → `_ =>` |

**Interfaces NOT changed:** `IReferenceCatalog.Changed` (stays `Action?`), `IBehaviorActionCatalog.Changed` (unaffected), `IActionSchemaExporter.Changed` (unaffected), `IEditableAsset.Changed` (unaffected).

## Suggested Commit Message

```
feat(main-toolbar): typed IAssetCatalog.Changed event + ReferenceCatalog Scenario-skip (MTB-P5-T1)

Change IAssetCatalog.Changed from Action? to Action<AssetKind>?. AssetCatalog
passes contributor.Kind through OnContributorChanged. ReferenceCatalog
early-returns on AssetKind.Scenario (no clear/rebuild/Changed fire). Update
all subscribers and 15+ test fakes across 5 projects. Add
ScenarioChange_DoesNotRebuild_References and NonScenarioChange_Rebuilds tests.

Co-Authored-By: Claude <noreply@anthropic.com>
```
