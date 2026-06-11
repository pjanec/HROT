# BATCH-14 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-11

## Summary
MTB-P5-T1: `IAssetCatalog.Changed` is now `Action<AssetKind>?`; `AssetCatalog` fires the changed
contributor's `Kind`; `ReferenceCatalog.OnCatalogChanged` early-returns on `AssetKind.Scenario`
(no AI-reference rebuild). All subscribers + test fakes updated to the new signature.

## Issues Found
No issues found.

## Verification (done by lead)
- **Full `dotnet build IOS-IG-SimHost.sln` → 0 errors, 0 warnings** — the signature change compiles
  across all subscribers/fakes solution-wide (the critical check).
- New tests run by lead: `ReferenceCatalogTests` → **14 passed, 0 failed**.
- Wiring read: `IAssetCatalog.Changed` = `event Action<AssetKind>?`; `AssetCatalog` wires
  `contributor.ContributorChanged += () => OnContributorChanged(contributor.Kind)` →
  `Changed?.Invoke(kind)`; `ReferenceCatalog.OnCatalogChanged(AssetKind kind)` → `if (kind ==
  AssetKind.Scenario) return;` BEFORE any clear/rebuild/Changed. Matches §10.4.
- `IReferenceCatalog.Changed` correctly left unchanged. Worker correctly distinguished the two
  `IAssetCatalog` types (AiShared vs Blueprints namespace) and updated only the AiShared fakes.
- Suites green: AiShared 925, Fdp.Toolkits 1856, SimHost 585, BTree.Editor 399, Hsm.Editor 352.
  `Hrot.Blueprints.Tests` still the established 9 PRE-1 pre-existing failures — no regression.

## Test Quality
Strong. `ScenarioChange_DoesNotRebuild_References` uses a recording `IReferenceCatalogContributor`
and asserts `EnumerateElementsCallCount`/`EnumerateReferencesCallCount` stay at 1 (the initial
non-scenario build) after firing `Changed(Scenario)` — i.e. NO walk on the scenario change — and
elements unchanged. `NonScenarioChange_Rebuilds` confirms the normal path. No tautological/skipped.

## Verdict
APPROVED. MTB-P5-T1 → `[x]`. Phase 5 continues (T3/T4/T5/T6 remain).

## Commit Message
```
feat(main-toolbar): typed IAssetCatalog.Changed(AssetKind) + ReferenceCatalog Scenario-skip (MTB-P5-T1)

IAssetCatalog.Changed now carries the changed AssetKind; AssetCatalog fires per-contributor Kind.
ReferenceCatalog.OnCatalogChanged early-returns on AssetKind.Scenario (scenario saves/creates no
longer trigger the full AI-reference rescan). All subscribers (ActionSchemaExporterCatalogWatcher,
AssetBrowserPanel, ReferenceCatalog) + 15 test fakes updated to the new signature; IReferenceCatalog
.Changed unchanged. Tests: ReferenceCatalogTests +2 (scenario-skip proven via recording contributor).
```
