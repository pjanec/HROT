# BATCH-14: Typed IAssetCatalog.Changed + ReferenceCatalog Scenario-skip
**Tasks:** MTB-P5-T1   **Phase:** 5   **Est:** ~5h
**Dependencies:** BATCH-13 (`AssetKind.Scenario` exists). DEC-10: T1 runs after T2.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §10.4 (typed change event; ReferenceCatalog ignores Scenario).
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → MTB-P5-T1.
4. Existing code (read):
   - `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalog.cs` — `event Action? Changed`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Catalog/AssetCatalog.cs` — `AddContributor` wires
     `contributor.ContributorChanged += OnContributorChanged`; `OnContributorChanged()` →
     `Changed?.Invoke()`.
   - Subscribers of `IAssetCatalog.Changed` (ALL must be updated to the new signature):
     `References/ReferenceCatalog.cs` (`OnCatalogChanged`, the one that must skip Scenario),
     `Blackboard/ActionSchemaExporterCatalogWatcher.cs` (`OnCatalogChanged`),
     `Browser/AssetBrowserPanel.cs` (`OnCatalogChanged`, ~L261/597).
   - Test subscribers using `catalog.Changed += () => ...` (parameterless) that must become
     `_ => ...`: `Catalog/AssetCatalogTests.cs`, `Catalog/AiAssetCatalogBuilderTests.cs`,
     `References/ReferenceCatalogTests.cs` (and any other `IAssetCatalog.Changed` lambda).

## Scope — do ONLY MTB-P5-T1
### 1. Typed event
- `IAssetCatalog.Changed`: `event Action? Changed` → **`event Action<AssetKind>? Changed`** (carry the
  `AssetKind` that changed). (Do NOT change `IReferenceCatalog.Changed` — it is a different interface
  and stays `Action?`.)
- `AssetCatalog`: pass the changed contributor's `Kind`. Wire
  `contributor.ContributorChanged += () => OnContributorChanged(contributor.Kind);` and
  `OnContributorChanged(AssetKind kind)` → rebuild cache (unchanged) then `Changed?.Invoke(kind)`.

### 2. ReferenceCatalog skips Scenario (§10.4)
- `ReferenceCatalog.OnCatalogChanged(AssetKind kind)`: **if `kind == AssetKind.Scenario`, return
  early** — do NOT clear/rebuild `_elements`/`_references`, do NOT walk contributors, and do NOT fire
  `Changed`. For any other kind, perform the existing clear+rebuild+`Changed?.Invoke()` exactly as today.

### 3. Update the remaining subscribers (behavior preserved)
- `ActionSchemaExporterCatalogWatcher.OnCatalogChanged(AssetKind kind)` — accept the arg; keep current
  behavior (it may ignore the kind, or you may also short-circuit on Scenario if its export is
  AI-only — but DO NOT change its observable behavior beyond the signature unless §10.4 requires it;
  default: keep behavior, ignore the arg).
- `AssetBrowserPanel.OnCatalogChanged(AssetKind kind)` — accept the arg; keep rebuilding (the panel
  shows all kinds incl. Scenario, so it should still refresh; ignoring the arg and rebuilding is fine).
- All test lambdas: `catalog.Changed += () => …` → `catalog.Changed += _ => …` (preserve assertions).

## Tests required (`ReferenceCatalogTests`)
- `ScenarioChange_DoesNotRebuild_References` — populate the reference catalog (non-scenario), then
  fire the asset catalog's `Changed(AssetKind.Scenario)`; assert `_elements`/`AllElements` and
  references are UNCHANGED (no contributor walk happened — use a recording fake
  `IReferenceCatalogContributor` and assert its `EnumerateElements`/`EnumerateReferences` were NOT
  called for the scenario change), and `ReferenceCatalog.Changed` did NOT fire.
- `NonScenarioChange_Rebuilds` — firing `Changed(AssetKind.Blueprint)` performs the clear+rebuild
  (contributor walk happens; elements/refs reflect the catalog) — existing behavior preserved.
- Confirm **all existing subscribers and tests compile and pass** against the new signature.

## Hard constraints
- Do NOT change `IReferenceCatalog.Changed`. Do NOT delete/modify legacy/assembly-loading code.
- No scope creep beyond the event signature + the subscriber updates + ReferenceCatalog skip + tests.
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings) — the signature change must compile
  across ALL subscribers.
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for `Hrot.Editor.AiShared.Tests` + the hot suites `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests`
  (PRE-3 EQS flake → re-run if it appears). Run any indirectly-touched suite by class filter; do NOT
  touch pre-existing failures.
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-14-REPORT.md`: files changed, the full subscriber list
  updated, the ReferenceCatalog skip logic, each new test + assertions, paste actual test-run
  summaries, insights.

If something cannot be done as specified, stop and report why rather than stubbing it.
