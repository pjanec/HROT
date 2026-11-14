# BATCH-13 Report

## Implementation Summary

### MTB-P5-T2: `AssetKind.Scenario` + `ScenarioCatalogContributor`

**Task 1 — Add `Scenario` to `AssetKind`:**
Added `Scenario` enum value to `AssetKind.cs`. Audited every `AssetKind` switch/use across the codebase and ensured each compiles + behaves correctly.

**Task 2 — Reconcile DEC-2 deferrals:**
- `AssetRoots.RecipesRelative`: added `Scenario → "Recipes/Scenarios"` arm (delegates to `ScenariosRecipesRelative`).
- `AssetRoots.AssetsFor(Scenario)` / `AssetsRelative(Scenario)`: still throws `ArgumentOutOfRangeException` — Scenario has no Assets root, by design.
- `AssetKindIcons.GetIconKey`: added `Scenario → ScenarioIconKey` arm; `ScenarioIconKey` constant preserved for backward compat.
- `AssetBrowserPanel.AssetKindFilterMapping.FromKind`: added `Scenario → AssetKindFilter.Scenario` arm.
- `AssetBrowserPanel.AssetKindFilterMapping.PermittedKinds`: now includes `Scenario` when the `Scenario` filter flag is set.
- Updated `AssetKindFilter.Scenario` comments (no longer "reserved").

**Task 3 — `ScenarioCatalogContributor`:**
Created in `Hrot/Subsystems/Hrot.Editor/Catalog/ScenarioCatalogContributor.cs` (the editor-host assembly, NOT AiShared). Implements `IAssetCatalogContributor`:
- `Kind => AssetKind.Scenario`; `BaseFolder => null`
- `Enumerate()`: projects the editor-side scenario list (via injected `Func<IReadOnlyList<string>>`) into one `IEditableAsset` per scenario
- Asset `Name` = scenario relative path verbatim (may contain `/`)
- `SourceFilePath` = `""`, `IsEditorOwned = false`, `IsDirty = false`
- `AssetId` = deterministic `Guid` via SHA256(UTF8(relpath))[:16]
- `Refresh()`: compares current list against last enumeration; fires `ContributorChanged` only when changed
- Registered in `EditorSubsystem.Initialize` via `_aiCatalogBuilder.Catalog.AddContributor()`

## Design Decisions

1. **AssetId derivation:** SHA256(UTF8(relpath)) → truncate to 16 bytes → Guid. This gives deterministic, stable IDs across enumeration calls and restarts, avoiding GUID generation entropy.

2. **ContributorChanged fires only on actual change:** Unlike the file-backed contributors that fire on every `Refresh()`, the scenario contributor compares the current list against the last-enumerated list and only fires when the content or order differs. This avoids unnecessary catalog rebuilds when the scenario list hasn't changed.

3. **Testability via `Func<IReadOnlyList<string>>`:** The contributor accepts a delegate for the scenario list source, not `IEditorLogic` directly. Tests inject a lambda over a mutable `List<string>`. No new interface was needed — the delegate is narrow and sufficient.

4. **Registration point:** The contributor is added to the catalog immediately after `AiAssetCatalogBuilder` construction in `EditorSubsystem.Initialize`. The source lambda captures `_editorLogic` (field, not value), so it works lazily when `Enumerate()`/`Refresh()` is called after `_editorLogic` is assigned later in the same init method.

## Deviations

- **EditorSubsystem.cs:2277 and :2435 switches gained `default: break;` arms.** The batch didn't explicitly require this for these two switches (they aren't switch expressions), but the audit guideline says "Any [switch] that would now miss a case → add a Scenario arm or a safe default." These two `switch` statements previously had no `default`, so `Scenario` would silently fall through. Added explicit `default: break;` with comments so future maintainers know the intent. Benefit: defensive against future enum additions; zero behavior change.

## Test Results

### New tests (unfiltered):

| Suite | Test | Passed |
|-------|------|--------|
| `Hrot.Editor.Tests` | `ScenarioContributorTests` | **13/13** |
| `Hrot.Editor.AiShared.Tests` | `AssetRootsTests` (updated) | **23/23** |
| `Hrot.Editor.AiShared.Tests` | `IconKeysTests` (updated) | **9/9** |
| `Hrot.Editor.AiShared.Tests` | `AssetBrowserPanelTests` (updated) | **16/16** |

### Full suites with Stability filter (0-failed):

| Suite | Passed | Skipped | Duration |
|-------|--------|---------|----------|
| `Hrot.Editor.AiShared.Tests` | 923 | 0 | 5s |
| `Hrot.Editor.Tests` | 129 | 0 | 0.8s |
| `Fdp.Toolkits.Tests` | 1856 | 0 | 31s |
| `Hrot.SimHost.Tests` | 585 | 3 | 12s |
| `Hrot.Blueprints.Tests` (affected classes) | 74 | 0 | 0.2s |

No new test failures. The pre-existing `EqsModuleTests.CognitiveSpatialModule_ResolvesAreaQuery...` flake appeared once in `Hrot.SimHost.Tests` and passed on re-run (confirmed known Stability/Flaky).

### Full solution build:
- **0 errors, 0 new warnings** (20 pre-existing warnings unrelated to this batch).

## AssetKind Switch Audit

| # | File | Switch type | Scenario handling | Status |
|---|------|-------------|-------------------|--------|
| 1 | `AssetRoots.AssetsRelative` | Switch expression | `_ => throw` → Scenario throws (correct: no Assets root) | ✅ |
| 2 | `AssetRoots.RecipesRelative` | Switch expression | **NEW** `Scenario → Recipes/Scenarios` arm | ✅ |
| 3 | `AssetKindIcons.GetIconKey` | Switch expression | **NEW** `Scenario → "asset/scenario"` arm | ✅ |
| 4 | `AssetKindFilterMapping.FromKind` | Switch expression | **NEW** `Scenario → AssetKindFilter.Scenario` arm | ✅ |
| 5 | `AssetKindFilterMapping.PermittedKinds` | Conditional | **NEW** Scenario included when flag set | ✅ |
| 6 | `AssetKindExtensions.ToPerspectiveName` | Switch expression | `_ => kind.ToString()` → returns "Scenario" | ✅ |
| 7 | `SaveAllAiDocumentsCommand.Execute` | Switch statement | `default` arm reports "unsupported kind" | ✅ |
| 8 | `ShellSaveCommands.Register` | Switch statement | `default` arm reports/skips unsupported kind | ✅ |
| 9 | `EditorSubsystem` save-on-close | Switch statement | **NEW** `default: break;` (safe, explicit) | ✅ |
| 10 | `EditorSubsystem` DocumentOpened | Switch statement | **NEW** `default: break;` (safe, explicit) | ✅ |

## Developer Insights

- **Issues resolved:** None. The implementation was straightforward — all DEC-2 deferrals were well-documented, making the reconciliation mechanical.

- **Nested test asset:** The `ScenarioEditableAsset` is a private nested class inside `ScenarioCatalogContributor`. This keeps the implementation self-contained and avoids leaking implementation details into the public API surface.

- **EditorSubsystem registration timing:** The `_editorLogic` field is assigned AFTER the builder in `Initialize()`, but the contributor captures the field (not its value at construction), so the lambda resolves correctly at call time.

- **Performance:** The contributor's `Refresh()` does an O(n) string comparison (SequenceEqual). For realistic scenario list sizes (< 100), this is negligible. If lists grow large, a hash-based comparison could be substituted.

## Known Issues

- None. The scenario contributor's `Refresh()` is not yet wired to any event — that's deferred to later batches that hook into scenario list change notifications. The catalog's initial `Enumerate()` during `AddContributor` works fine (returns empty list at init time, refreshed when scenarios become available).

## Suggested Commit Message

```
feat(main-toolbar): AssetKind.Scenario + ScenarioCatalogContributor + DEC-2 reconciliation (MTB-P5-T2)

Add Scenario to AssetKind enum; audit all 10 AssetKind switches; reconcile
AssetRoots.RecipesFor/RecipesRelative, AssetKindIcons.GetIconKey, and
AssetKindFilterMapping (FromKind + PermittedKinds) DEC-2 deferrals.
Add ScenarioCatalogContributor in Hrot.Editor (non-file-backed,
deterministic AssetId via SHA256, change-aware ContributorChanged).
13 new tests + updated AssetRoots/IconKeys/filter-mapping tests.
```

## Files Changed

1. `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKind.cs` — added `Scenario`
2. `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetRoots.cs` — `RecipesRelative` + doc updates
3. `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKindIcons.cs` — `GetIconKey` + doc updates
4. `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetBrowserPanel.cs` — `FromKind`, `PermittedKinds`, `AssetKindFilter` comment
5. `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — `default: break;` (×2 switches) + contributor registration
6. `Hrot/Subsystems/Hrot.Editor/Catalog/ScenarioCatalogContributor.cs` — **NEW**
7. `Hrot/Subsystems/Hrot.Editor.Tests/Catalog/ScenarioContributorTests.cs` — **NEW**
8. `Hrot/Editor/Hrot.Editor.AiShared.Tests/Identity/AssetRootsTests.cs` — Scenario arms
9. `Hrot/Editor/Hrot.Editor.AiShared.Tests/Adapters/IconKeysTests.cs` — Scenario arm
10. `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/AssetBrowserPanelTests.cs` — filter mapping tests
