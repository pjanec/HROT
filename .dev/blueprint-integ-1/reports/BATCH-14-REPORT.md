# BATCH-14 Report

## Implementation Summary

### AIE-051 — Reference catalog contributors + RefactorService + FindResults wiring

**New files:**
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Catalog/HsmReferenceContributor.cs` — implements `IReferenceCatalogContributor` for the HSM subsystem. `EnumerateElements` returns machine-scoped event sub-elements (key format: `{assetId:D}::{eventName}`). `EnumerateReferences` returns action FQN references from state OnEntry/OnExit/Activity/Timer actions, transition guard/action FQNs, and event usages from transitions + global transitions.
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Catalog/BlueprintReferenceContributor.cs` — implements `IReferenceCatalogContributor` for the Blueprint subsystem. Phase 1 scope: exposes each `BlueprintFileAsset` as a sub-element keyed by `assetId:D` (so peer-call references from other assets can resolve the target). Returns empty references (full per-node graph references require deserialized `BlueprintAsset`, deferred to AIE-053).

**Composition root (`EditorSubsystem.RegisterWindows`):**
```csharp
var referenceContributors = new IReferenceCatalogContributor[]
{
    new BTreeBlackboardVariableContributor(),
    new HsmReferenceContributor(),
    new BlueprintReferenceContributor(),
};
var referenceCatalog = new ReferenceCatalog(catalog, referenceContributors);
```
`RefactorService` and `FindResultsWindow` were already wired to `referenceCatalog` — they now see contributors-populated cross-asset references on every catalog rebuild.

---

### AIE-052 — Blackboard aggregator strategies

**`BlackboardAggregatorService.Register` made `public`** (was `internal`). The composition root lives in `Hrot.Editor` which is not `InternalsVisibleTo`-listed in `Hrot.Editor.AiShared.csproj`. Changed the doc comment to reflect the legitimate production use case. All existing usages (strategies pass their `BlackboardAggregatorService` reference to each other) remain correct.

**Composition root:**
```csharp
var aggregatorService = new BlackboardAggregatorService(
    Array.Empty<IBlackboardAggregatorStrategy>(),
    new ActionSchemaExporter(),
    catalog);
aggregatorService.Register(new BTreeBlackboardAggregatorStrategy(aggregatorService));
aggregatorService.Register(new HsmBlackboardAggregatorStrategy(aggregatorService));
```
Strategies registered after construction to break the circular dependency (strategies take the service in their ctor; service takes strategies).

**`BlackboardAuthoringWindow`** got a new optional ctor param `BlackboardAggregatorService? aggregatorService = null`. In `DrawClientArea`, the aggregation is called per-frame before `BuildViewModel`:
```csharp
var aggregationResult = (_aggregatorService != null && _store.ActiveAsset != null)
    ? _aggregatorService.Aggregate(_store.ActiveAsset)
    : (AggregationResult?)null;
var vm = BuildViewModel(_store.ActiveAsset, aggregationResult: aggregationResult);
```
`BuildViewModel` already accepted `AggregationResult?` and derived unbound requirement rows and bin-packing from it.

**`PerspectiveWorkspaceRegistrar`** got three new optional params: `SanitizerRegistry?`, `ComparisonExportBuilder?`, `ComparisonSessionRegistry?` (for AIE-050), and `BlackboardAggregatorService?` (for AIE-052). All forwarded to `BlackboardAuthoringWindow` construction.

BTree and HSM perspectives receive the aggregator service; Blueprint perspective does not (no HSM/BTree aggregation for Blueprint assets, and Blueprint is out of scope for bin-packing in v1).

---

### AIE-050 — Comparison sanitizers + ComparisonExportBuilder

**Composition root:**
```csharp
var sanitizerRegistry = new SanitizerRegistry();
sanitizerRegistry.Register(new BTreeComparisonSanitizer(catalog));
sanitizerRegistry.Register(new HsmComparisonSanitizer(catalog));
sanitizerRegistry.Register(new BlueprintComparisonSanitizer(
    new NoOpComparisonMigrationAdapter(),
    new NoOpMetaEnvelopeSanitizer(),
    catalog));
var comparisonExportBuilder = new ComparisonExportBuilder();
var comparisonSessionRegistry = new ComparisonSessionRegistry();
```

The three per-kind sanitizers are constructed directly, mirroring the DI-extension bodies from:
- `BTreeEditorComparisonServiceCollectionExtensions.AddBTreeEditorComparison`: `new BTreeComparisonSanitizer(catalog)`
- `HsmEditorComparisonServiceCollectionExtensions.AddHsmEditorComparison`: `new HsmComparisonSanitizer(catalog)`
- `BlueprintEditorComparisonServiceCollectionExtensions.AddBlueprintEditorComparison`: `new BlueprintComparisonSanitizer(migrationAdapter, metaSanitizer, catalog)` with `NoOp` implementations

`BlackboardAuthoringWindow` already had `SanitizerRegistry?`, `ComparisonExportBuilder?`, `ComparisonSessionRegistry?` ctor params and conditionally constructs a `ComparisonToolbarAction` — the existing window is the consumption point (no new window invented). The comparison services are now fed through `PerspectiveWorkspaceRegistrar` to all three perspectives.

---

## Design Decisions

1. **`BlueprintReferenceContributor` is header-only (Phase 1):** The catalog holds `BlueprintFileAsset` (header-only, no graphs). Full per-node graph reference enumeration (`CallPeerBlueprintNode` etc.) requires the deserialized `BlueprintAsset`, which is only available when a document is opened. This is consistent with the design-talk note and deferred to AIE-053.

2. **`Register` made public on `BlackboardAggregatorService`:** The original `internal` access was correct for test-bootstrapping but the production composition root also needs it for the same circular-dependency-breaking pattern. The change is additive and safe — the method is still synchronous and idempotent in behavior.

3. **Blueprint perspective does NOT receive the aggregator service:** `BlueprintAsset` doesn't implement `IBlackboardManagedAsset`; the bin-packing in `BlackboardAuthoringWindow` only activates for managed assets. Wiring aggregation to the Blueprint perspective would be a no-op and adds unnecessary coupling.

4. **`HsmReferenceContributor` includes global transitions:** The design-talk snippet showed normal transitions only. I added global transition support (Guard + Action FQNs + event refs) since `HsmAsset.AllGlobalTransitions` exposes the same fields.

---

## Deviations

| What | Why | Benefit | Risk |
|---|---|---|---|
| `BlueprintReferenceContributor` returns empty references (header-only) | Full graph not available in catalog | No false deserialization; matches Phase 1 scope | Full per-node reference tracking not available until AIE-053 |
| `Register` made `public` on `BlackboardAggregatorService` | Composition root is in a different assembly without `InternalsVisibleTo` | Clean production wiring without reflection hacks | None; the method was always intended as part of the construction protocol |

---

## Test Results

### New test files

| File | Tests | What they verify |
|---|---|---|
| `Hrot.BTree.Editor.Tests/Catalog/ReferenceCatalogCrossAssetTests.cs` | 3 | (1) Cross-asset reference via `Contribute`: host/target ids asserted; (2) Multi-ref: two assets referencing one target; (3) `BTreeBlackboardVariableContributor` intra-asset: element key format + reference target key + round-trip through `ReferenceCatalog.Contribute` |
| `Hrot.Editor.AiShared.Tests/Refactor/Batch14RefactorTests.cs` | 2 | `ApplyRename` atomic write: asserts exact written-file set + "action://NewKey" present / "action://OldKey" absent in all 3 files; partial-match: only target file rewritten |
| `Hrot.Editor.AiShared.Tests/Blackboard/Batch14AggregatorBinPackTests.cs` | 4 | (1) Aggregated requirements that fit inline → no warning; (2) BigDto (104 bytes) doesn't fit inline → `RequiresHeavyComponent = true`; (3) Master vars overflow inline budget → `PackWarning.InlineMemoryExceeded`; (4) Unbound requirements carry correct `DtoType` + provenance (path, assetId, elementId) |
| `Hrot.Editor.AiShared.Tests/Comparison/Batch14SanitizerRegistryTests.cs` | 7 | `SanitizerRegistry.Get` returns sanitizer for BTree/HSM/Blueprint with correct `TargetKind`; BTree/HSM/Blueprint sanitizers produce deterministic output (bit-for-bit identical on second call with same input file) |

### Suite results

| Suite | Pass | Fail | Notes |
|---|---|---|---|
| `Hrot.Editor.AiShared.Tests` | **718** | 0 | +16 vs BATCH-13 baseline of 702 |
| `Hrot.BTree.Editor.Tests` | **380** | 0 | +3 vs BATCH-13 baseline of 377 |
| `Hrot.Hsm.Editor.Tests` | **330** | 0 | unchanged |
| `Hrot.Blueprints.Tests` | **1027** | **10** | exactly the pre-existing DEBT-006 failures; 0 new failures |
| `EditorSubsystemBoot` | **10** | 0 | unchanged |

### Build

`dotnet build IOS-IG-SimHost.sln` → **0 errors / 0 warnings** (all production assemblies). GizmoMap.Contracts remains on 0.2.2, Hrot.IG/DDS untouched.

---

## Developer Insights

1. **`BTreeBlackboardVariableContributor` is intra-asset only.** The `TargetKey` format `{hostAssetId}::{variableName}` means every reference points to the declaring asset. True cross-asset variable references (e.g., subtree-shared variables) would require a different key format where the referencing node stores the *target* asset's id — not the host's. This is by design for the rename flow (rename the variable on the declaring asset, update all references within that same asset).

2. **Event-driven `ReferenceCatalog` population depends on `IAssetCatalog.Changed` being fired.** During test bootstrapping, calling `FireChanged()` manually on a `TestCatalog` is idiomatic and mirrors the production flow (where `AiCatalogBuilder.Reload()` fires the event). The `ReferenceCatalog` does not populate itself at construction time — this is correct but must be documented for test authors.

3. **`BlackboardBinPacker` behavior for aggregated vars.** Aggregated variables do NOT unconditionally raise `InlineMemoryExceeded` — they spill to the heavy tier when they don't fit inline. Only master variables can cause `InlineMemoryExceeded`. Budget overflow for aggregated requirements surfaces as `RequiresHeavyComponent = true`, which the window should display as a budget advisory (not an error). Test `BuildViewModel_AggregatedRequirements_DontFitInline_RequiresHeavyComponent` covers this accurately.

4. **`NoOpComparisonMigrationAdapter` + `NoOpMetaEnvelopeSanitizer` are the correct Phase 1 implementations.** Both are already present in `Hrot.Editor.AiShared` and are documented as the v1 stubs until migration/meta systems land.

---

## Known Issues

- **`BlueprintReferenceContributor` returns no edge references** until the document manager hydrates the full `BlueprintAsset`. Cross-asset peer-call tracking (CallPeerBlueprintNode references) is deferred to AIE-053.
- **`HsmReferenceContributor` does not cover HSM blackboard variables** — only events/actions/guards. If HSM blackboard variables need the same refactor support as BTree, a separate `HsmBlackboardVariableContributor` would be needed (analogous to `BTreeBlackboardVariableContributor`).

---

## Suggested Commit Message

```
feat(editor): wire reference contributors, blackboard aggregator, and comparison sanitizers (BATCH-14, AIE-050/051/052)

AIE-051: HsmReferenceContributor (events + action/guard FQNs) and BlueprintReferenceContributor
(header-only asset-id sub-elements) added; BTreeBlackboardVariableContributor, HsmReferenceContributor,
BlueprintReferenceContributor passed into ReferenceCatalog ctor in EditorSubsystem.

AIE-052: BlackboardAggregatorService constructed with BTreeBlackboardAggregatorStrategy +
HsmBlackboardAggregatorStrategy; BlackboardAuthoringWindow + PerspectiveWorkspaceRegistrar accept
aggregatorService and pass it to BuildViewModel so bin-packing surfaces RequiresHeavyComponent
and budget warnings from sub-tree DTO requirements.

AIE-050: SanitizerRegistry constructed with BTreeComparisonSanitizer, HsmComparisonSanitizer,
BlueprintComparisonSanitizer (NoOp adapters); ComparisonExportBuilder + ComparisonSessionRegistry
constructed and fed through PerspectiveWorkspaceRegistrar to BlackboardAuthoringWindow comparison toolbar.

Build: 0 errors / 0 warnings. Tests: AiShared 718/0 (+16), BTree 380/0 (+3), Hsm 330/0,
Blueprints 1027/10 (DEBT-006 only), EditorSubsystemBoot 10/0.
```
