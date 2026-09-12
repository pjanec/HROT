# BATCH-01 Report

## Implementation Summary

Added `AssetRoots` static class in `Hrot.Editor.AiShared/Identity/AssetRoots.cs` — the single
authority for the two root families described in DESIGN.md §16:

- **`AssetsRoot`** — absolute path to the `Assets/` root (final assets: browse/save destination).
- **`RecipesRoot`** — absolute path to the `Recipes/` root (creation sources).
- **`AssetsFor(AssetKind)`** — returns `Assets/{Blueprints|HSMs|BTrees}` for the three file kinds.
  Throws `ArgumentOutOfRangeException` for Blackboard and Utility (no Assets root defined).
- **`RecipesFor(AssetKind)`** — returns `Recipes/{Blueprints|HSMs|BTrees}` for the three file kinds.
  Throws `ArgumentOutOfRangeException` for Blackboard and Utility.
- **`ScenariosRecipesRoot`** — dedicated member `Recipes/Scenarios` (Scenario seed root). Scenario
  has no Assets root; this is its only root until `AssetKind.Scenario` is added in MTB-P5-T2.

All public members are XML-documented. No `AssetKind.Scenario` was added (per DEV-LEAD decision).
No runtime behavior was changed anywhere — this batch is constants only.

**Tests** (10 tests in `Hrot.Editor.AiShared.Tests/Identity/AssetRootsTests.cs`):
- `AssetsFor_EachFileKind_ReturnsExpectedRelativeSegment` — verifies Blueprint→`Assets/Blueprints`,
  Hsm→`Assets/HSMs`, BTree→`Assets/BTrees`.
- `RecipesFor_AllKinds_IncludingScenario` — verifies Blueprint→`Recipes/Blueprints`,
  Hsm→`Recipes/HSMs`, BTree→`Recipes/BTrees`, and `ScenariosRecipesRoot`→`Recipes/Scenarios`.
- `AssetsFor_Scenario_HasNoAssetsRoot` — verifies `ScenariosRecipesRoot` is under `RecipesRoot`,
  NOT `AssetsRoot`; `AssetsFor(Blackboard|Utility)` throw `ArgumentOutOfRangeException`.
- `AssetsRoot_And_RecipesRoot_AreDisjoint` — verifies the two roots are different, non-empty,
  absolute paths, and neither is a subpath of the other (§16 disjoint-roots invariant).
- Plus 3 additional tests for exception param-names, non-emptiness, and absolute-path rooting.

Assertions check **actual returned path values** using `Path`-normalized suffix comparison
(EndsWith), `StartsWith`, `Path.IsPathRooted`, and `Assert.Throws<ArgumentOutOfRangeException>`
with `ParamName` verification.

## Design Decisions

**Root resolution: `AppContext.BaseDirectory`** (not `typeof(Hrot.AI.Behaviors type).Assembly.Location`).

Per the DEV-LEAD decision in the batch spec: prefer `typeof(Hrot.AI.Behaviors type).Assembly.Location`
**if** `Hrot.Editor.AiShared` already references `Hrot.AI.Behaviors`. Inspection of
`Hrot.Editor.AiShared.csproj` shows it does **not** reference `Hrot.AI.Behaviors`, so the fallback
`AppContext.BaseDirectory` is the correct choice. Both assemblies deploy to the same output
directory at runtime, so the result is identical.

## Deviations

**None.** The implementation follows the batch spec exactly:
- API surface matches the Required API section.
- Placement in `Hrot.Editor.AiShared/Identity/` per the DEV-LEAD decision.
- Root resolution via `AppContext.BaseDirectory` per the documented fallback.
- No `AssetKind.Scenario` added; `ScenariosRecipesRoot` as dedicated member.
- Blackboard/Utility throw `ArgumentOutOfRangeException`.
- No files moved, no `.csproj` globs changed, no consumers repointed.
- No legacy/assembly-loading code touched.

## Test Results

### AssetRoots tests (unfiltered — new code must pass without Stability traits)

```
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests --filter "FullyQualifiedName~AssetRootsTests"
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: 24 ms
```

### Full Hrot.Editor.AiShared.Tests (with Stability filter)

```
dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
Passed!  - Failed: 0, Passed: 866, Skipped: 0, Total: 866, Duration: 4 s
```

### Fdp.Toolkits.Tests (catalogued hot suite)

```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
Passed!  - Failed: 0, Passed: 1856, Skipped: 0, Total: 1856, Duration: 20 s
```

### Hrot.SimHost.Tests (catalogued hot suite)

```
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
Passed!  - Failed: 0, Passed: 585, Skipped: 3, Total: 588, Duration: 12 s
```

The 3 skipped tests (`SimHostSubsystem_InitializeHeadless_DoesNotThrow`,
`CgfSubsystem_InitializeHeadless_DoesNotThrow`, `OnLoad_RegistersFireInteractionEventTranslator`)
are pre-existing skips, unrelated to this batch.

### Full solution build

```
dotnet build IOS-IG-SimHost.sln
Build succeeded.  13 Warning(s)  0 Error(s)
```

All 13 warnings are pre-existing in unrelated test projects (xUnit2013 in
`Hrot.Utility.Editor.Tests`, CS0618 in `Hrot.Diagnostics.Breakpoints.Tests` and
`Hrot.Blueprints.Tests`, CS8601/CS8602 in `Hrot.Blueprints.Tests`). Zero warnings from this
batch's code. No `BLUEPRINT_REGENERATE_SNAPSHOTS` was set.

## Developer Insights

- **The existing test pattern is clean** — `Hrot.Editor.AiShared.Tests` has a straightforward
  xunit + FluentAssertions setup with `GlobalUsings.cs`. The Identity subfolder already has
  `AssetKindExtensionsTests.cs` and `AssetIdHashTests.cs`, so `AssetRootsTests.cs` fits naturally.
- **Path comparison on Windows requires care** — used `Replace('/', Path.DirectorySeparatorChar)`
  on both sides of every assertion to make tests pass regardless of whether `Path.Combine`
  produces `\` or `/`. The `AssertEndsWithRelative` helper handles both cases.
- **No edge cases beyond the spec were discovered** — the `AssetKind` enum is small (5 values),
  two have no roots, three have both, Scenario is deferred. The API is straightforward.
- **`AppContext.BaseDirectory` in tests** resolves to the test output directory (e.g.
  `bin/Debug/net8.0/`). The tests don't require the actual folders to exist — they only verify
  path composition, which is correct for this "constants only" batch.

## Known Issues

- **Scenario enum gap**: `ScenariosRecipesRoot` is a dedicated member rather than
  `RecipesFor(AssetKind.Scenario)`. This is intentional per the batch spec and will be reconciled
  in MTB-P5-T2 when `AssetKind.Scenario` is added.
- **Consumer repointing deferred**: All existing code that uses hardcoded paths
  (`Blueprints/Recipes`, `Machines`, `Trees`, etc.) still uses string literals. MTB-P0-T3 will
  repoint them to `AssetRoots`.

## Suggested Commit Message

```
feat(AssetRoots): add AssetRoots constants class with Assets/Recipes root resolution
```

**Files changed:**
- `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetRoots.cs` (NEW)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Identity/AssetRootsTests.cs` (NEW)
