# BATCH-14 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-02

## Summary
Phase 5 wiring of three existing cross-asset services into the editor composition root:
- **AIE-051** — `ReferenceCatalog` now receives contributors (`BTreeBlackboardVariableContributor`, new `HsmReferenceContributor`, new `BlueprintReferenceContributor`); `RefactorService`/`FindResultsWindow` were already wired and now see populated references.
- **AIE-052** — `BlackboardAggregatorService` constructed with BTree+HSM strategies (post-ctor `Register` to break the service↔strategy cycle); `BlackboardAuthoringWindow` + `PerspectiveWorkspaceRegistrar` gained optional aggregator param feeding bin-packing.
- **AIE-050** — `SanitizerRegistry` populated with BTree/HSM/Blueprint sanitizers (mirroring the DI-extension bodies, since the editor composes manually); `ComparisonExportBuilder` + `ComparisonSessionRegistry` constructed and forwarded to the existing `BlackboardAuthoringWindow` comparison toolbar (no new window invented).

## Verification performed (ran myself)
- **`dotnet build IOS-IG-SimHost.sln` → Build succeeded, 0 Warnings, 0 Errors** (GizmoMap.Contracts on 0.2.2; Hrot.IG/DDS untouched).
- `Hrot.Editor.AiShared.Tests` **718 / 0** (+16). `Hrot.BTree.Editor.Tests` **380 / 0** (+3). `Hrot.Hsm.Editor.Tests` **330 / 0**. `Hrot.Blueprints.Tests` **1027 / 10 / 8** — the 10 are the pre-existing DEBT-006 set (golden emit, allocation-free, library/MoveToAndFire snapshots; same count as the BATCH-13 baseline, **no new failures**). `EditorSubsystemBoot` **10 / 0**.

## Code read (diffs + new files)
- `EditorSubsystem.RegisterWindows`: contributors array → `new ReferenceCatalog(catalog, contributors)`; `SanitizerRegistry` + 3 sanitizers + builder/session registry; `BlackboardAggregatorService` + 2 strategies via post-ctor `Register`; all forwarded through `PerspectiveWorkspaceRegistrar` (BTree/HSM get aggregator; Blueprint gets comparison only — justified: BlueprintAsset isn't an `IBlackboardManagedAsset`).
- `HsmReferenceContributor`: real `HsmAsset` members (AllEvents/AllStates/AllTransitions/AllGlobalTransitions/FindEventById; state On{Entry,Exit}/Activity/Timer actions; transition Guard/Action FQNs); machine-scoped event keys `{assetId:D}::{name}`. Compiles + tested.
- `BlueprintReferenceContributor`: header-only (catalog holds `BlueprintFileAsset`), exposes asset-id sub-element keyed `{assetId:D}` (matches `CallPeerBlueprintNode.PeerBlueprintId` format); returns no edge references — full per-node tracking explicitly deferred (see Debt below).
- `BlackboardAggregatorService.Register` made `public` (was `internal`) — composition root is in a non-`InternalsVisibleTo` assembly; additive, part of the construction protocol.

## Test quality (read assertions)
- `ReferenceCatalogCrossAssetTests` (BTree): asserts host/target ids on cross-asset references + element-key/target-key round-trip through `ReferenceCatalog.Contribute`.
- `Batch14RefactorTests`: `ApplyRename` atomic write asserts exact written-file set + `action://NewKey` present / `OldKey` absent in all files; partial-match rewrites only the target file.
- `Batch14AggregatorBinPackTests`: fit→no warning; BigDto→`RequiresHeavyComponent`; master-var overflow→`InlineMemoryExceeded`; unbound requirement carries `DtoType`+provenance (path/assetId/elementId).
- `Batch14SanitizerRegistryTests`: per-kind sanitizer presence + correct `TargetKind`; deterministic (bit-for-bit) stripped output on repeat. Real-value assertions, not non-null.

## Issues Found
None blocking.

## Debt logged
- **DEBT-011 (P2):** `BlueprintReferenceContributor` returns no edge references until the full `BlueprintAsset` is hydrated — cross-asset peer-call (CallPeerBlueprintNode) tracking deferred to AIE-053/BATCH-15.
- **DEBT-012 (P3):** No `HsmBlackboardVariableContributor` — HSM blackboard variables lack the rename support BTree variables have. Out of scope for v1.

## Verdict
APPROVED. Only AIE-053 (collision detector + dangling-reference classification, partly net-new) remains in Phase 5 → BATCH-15.

## Commit Message
```
feat(editor): wire reference contributors, blackboard aggregator, comparison sanitizers (BATCH-14, AIE-050/051/052)

AIE-051: HsmReferenceContributor (events + action/guard FQNs incl. global transitions) and
BlueprintReferenceContributor (header-only asset-id sub-elements) added; all three contributors
passed into the ReferenceCatalog ctor in EditorSubsystem.

AIE-052: BlackboardAggregatorService constructed with BTree+HSM strategies (post-ctor Register to
break the cycle); BlackboardAuthoringWindow + PerspectiveWorkspaceRegistrar accept the aggregator
and feed bin-packing so sub-tree DTO requirements surface budget warnings.

AIE-050: SanitizerRegistry populated with BTree/HSM/Blueprint sanitizers (NoOp adapters), plus
ComparisonExportBuilder + ComparisonSessionRegistry, forwarded to the existing comparison toolbar.

Build: 0 errors / 0 warnings. Tests: AiShared 718/0 (+16), BTree 380/0 (+3), Hsm 330/0,
Blueprints 1027/10 (DEBT-006 only), EditorSubsystemBoot 10/0. DEBT-011/012 logged.
```
