# BATCH-13: AssetKind.Scenario + ScenarioCatalogContributor
**Tasks:** MTB-P5-T2   **Phase:** 5 — Hosts, Scenarios, Typed Change, Wiring   **Est:** ~8h
**Dependencies:** Phase 4. **Runs BEFORE MTB-P5-T1** (DEC-10: T1's ReferenceCatalog must skip the
`AssetKind.Scenario` this batch introduces).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §10.4 (AssetKind.Scenario, contributor) and §19 (scenario nested paths context).
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → MTB-P5-T2.
4. Existing code (read):
   - `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKind.cs` — enum (Blueprint/BTree/Hsm/Blackboard/Utility).
   - `Hrot/Editor/Hrot.Editor.AiShared/Identity/IEditableAsset.cs` — `AssetId,Name,Kind,SourceFilePath,IsDirty,IsEditorOwned,Changed`.
   - `Hrot/Editor/Hrot.Editor.AiShared/Catalog/IAssetCatalogContributor.cs` — `Kind`, `Enumerate`,
     `BaseFolder` (default null), `ContributorChanged`.
   - `Hrot/Subsystems/Hrot.Editor/IEditorLogic.cs` — `IReadOnlyList<string> AvailableScenarios`
     (scenario names in the local scenarios root; names may contain `/` per §19).
   - Deferred Scenario arms to reconcile (DEC-2): `AssetRoots.RecipesFor`, `AssetKindIcons.GetIconKey`/
     `ScenarioIconKey`, `AssetBrowserPanel.AssetKindFilterMapping` (`FromKind`/`PermittedKinds`).
   - The 6 `AssetKind` switches (both have a `default` arm already):
     `Documents/SaveAllAiDocumentsCommand.cs`, `Documents/ShellSaveCommands.cs`.

## Scope — do ONLY MTB-P5-T2. Do NOT change `IAssetCatalog.Changed` (that is MTB-P5-T1/BATCH-14).

### 1. Add `Scenario` to `AssetKind`
- Add `Scenario` to the enum. **Audit every `AssetKind` switch/use** and ensure each compiles and
  behaves correctly with the new value:
  - `SaveAllAiDocumentsCommand` / `ShellSaveCommands`: their `default` arm already reports/skips
    unsupported kinds — scenarios are not saved via these (scenario save is separate). Confirm the
    `default` handles Scenario gracefully (no throw on a normal path). Leave save-routing as-is.
  - Any exhaustive switch that would now miss a case → add a Scenario arm or a safe default.

### 2. Reconcile the DEC-2 deferrals now that the enum exists
- `AssetRoots`: add a `Scenario` arm to **`RecipesFor`** → `ScenariosRecipesRoot` (= `Recipes/Scenarios`)
  and to **`RecipesRelative`** → `"Recipes/Scenarios"`. Scenario still has **no Assets root** —
  `AssetsFor(AssetKind.Scenario)`/`AssetsRelative` must still throw `ArgumentOutOfRangeException`
  (document: scenarios are orchestrator/NAS-backed). Update/extend `AssetRootsTests` accordingly
  (Scenario now resolvable via `RecipesFor`; `AssetsFor(Scenario)` throws).
- `AssetKindIcons.GetIconKey`: add a `Scenario → "asset/scenario"` arm (the `ScenarioIconKey`
  constant may remain or be folded — keep behavior, update its test to use the enum arm).
- `AssetBrowserPanel.AssetKindFilterMapping`: map `AssetKind.Scenario ↔ AssetKindFilter.Scenario` in
  `FromKind` and include it in `PermittedKinds` when the `Scenario` flag is set.

### 3. `ScenarioCatalogContributor` (editor-host assembly — NOT AiShared)
**File (NEW):** in `Hrot.Editor` (the editor-host that knows the scenario list; keep AiShared free of
an orchestrator dependency). Implements `IAssetCatalogContributor`:
- `Kind => AssetKind.Scenario`; `BaseFolder => null` (no Assets root for scenarios).
- `Enumerate()`: project the editor-side scenario list (from `IEditorLogic.AvailableScenarios`, or a
  narrow injected `Func<IReadOnlyList<string>>`/abstraction so it is unit-testable with a fake) into
  one `IEditableAsset` per scenario: `Name` = scenario **relative path** (verbatim, may contain `/`),
  `SourceFilePath` = `""` (empty), `IsEditorOwned = false`, `IsDirty = false`, `Kind = Scenario`,
  `AssetId` = a **deterministic** Guid derived from the relpath (so `FindByAssetId` is stable across
  refreshes — document the derivation).
- Raises `ContributorChanged` when the projected list changes (compare against the previous
  enumeration on a `Refresh()`/notification). Provide a way to trigger re-enumeration testably.

### Tests required (`ScenarioContributorTests`, in the test project matching the chosen assembly,
with a fake scenario-list source)
- `Kind_IsScenario` — `contributor.Kind == AssetKind.Scenario`.
- `Enumerate_OneAssetPerScenario_NameIsRelPath` — given scenario list
  `["alpha", "campaign/beta", "campaign/sub/gamma"]`, `Enumerate()` returns 3 assets with `Name`
  equal to each relpath verbatim, `SourceFilePath` empty, `IsEditorOwned == false`,
  `Kind == Scenario`; AssetIds are stable across two `Enumerate()` calls.
- `ContributorChanged_FiresOnListChange` — changing the underlying list and triggering re-enumeration
  raises `ContributorChanged`; no event when the list is unchanged.
Also add/extend `AssetRootsTests` for the new Scenario `RecipesFor` arm (and that `AssetsFor(Scenario)`
still throws), and update `AssetKindIcons`/filter-mapping tests for the Scenario arms.

## Hard constraints
- Do NOT change `IAssetCatalog.Changed` signature (MTB-P5-T1). Do NOT delete/modify legacy/assembly
  code. Keep `ScenarioCatalogContributor` out of `Hrot.Editor.AiShared` (layering — AiShared must not
  depend on the orchestrator/editor-host).
- Adding `Scenario` must not break any existing `AssetKind` switch (verify all compile + behave).
- Do NOT weaken/skip/auto-pass tests; zero new warnings (TreatWarningsAsErrors).

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. New tests pass UNFILTERED. 0-failed with the Stability
  filter for `Hrot.Editor.AiShared.Tests`, the contributor's test project, and the hot suites
  `Fdp.Toolkits.Tests` + `Hrot.SimHost.Tests` (PRE-3 EQS flake → re-run if it appears). Note: adding
  the enum value may touch `Fdp.Presentation.Tests`/`Hrot.Blueprints.Tests` indirectly — run affected
  suites by class filter (PRE-1/PRE-2/PRE-4); do NOT touch pre-existing failures.
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-13-REPORT.md`: files changed, the AssetKind switch audit
  (each switch + how Scenario is handled), the contributor's assembly + scenario-list source + AssetId
  derivation, the DEC-2 reconciliations, each new/updated test + assertions, paste actual test-run
  summaries, insights.

If something cannot be done as specified, stop and report why rather than stubbing it.
