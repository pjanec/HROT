# BATCH-01: AssetRoots constants
**Tasks:** MTB-P0-T1   **Phase:** 0 — Folder Reorganization   **Est:** ~4–6h
**Dependencies:** none (foundation for MTB-P0-T2/T3)

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract (autonomy, test quality, report).
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §16 (Folder Reorganization) and §13 (Layering & Assembly Placement) — the rationale. Do **not** re-derive; just implement to it.
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → "MTB-P0-T1 — `AssetRoots` constants" — the acceptance bar.

## Scope — do ONLY this
Add an `AssetRoots` static class that is the single authority for the two root families
described in §16:
```
Assets/   { Blueprints, HSMs, BTrees }     ← final assets (browse/save destination)
Recipes/  { Blueprints, HSMs, BTrees, Scenarios }  ← creation sources
```
This batch is **constants only** — it adds the class + tests and changes **no** runtime
behavior anywhere else (consumers are repointed later in MTB-P0-T3; files are moved in
MTB-P0-T2). Do not touch any contributor, bootstrap, csproj glob, or move any file.

### Required API
- `AssetRoots.AssetsRoot` — absolute path to the `Assets/` root.
- `AssetRoots.RecipesRoot` — absolute path to the `Recipes/` root.
- `AssetsFor(AssetKind kind)` → `Assets/{Blueprints|HSMs|BTrees}` for the three **file** kinds
  (Blueprint→`Assets/Blueprints`, Hsm→`Assets/HSMs`, BTree→`Assets/BTrees`).
- `RecipesFor(AssetKind kind)` → `Recipes/{Blueprints|HSMs|BTrees}` for the three file kinds.
- `ScenariosRecipesRoot` → `Recipes/Scenarios` (Scenario seed root).
- Scenario has **no** Assets root — see DECISION below for how to express this without
  `AssetKind.Scenario`.
- For kinds with no defined root in §16 (`Blackboard`, `Utility`): throw
  `ArgumentOutOfRangeException` (documented) rather than returning a wrong/guessed path.

### Placement (DEV-LEAD DECISION — follow exactly)
- Put `AssetRoots` in **`Hrot.Editor.AiShared`** (e.g. `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetRoots.cs`),
  NOT in `Hrot.AI.Behaviors`.
  **Why:** the API is keyed by `AssetKind`, which lives in `Hrot.Editor.AiShared`.
  `Hrot.AI.Behaviors` is a game-side assembly that must not reference editor code, so it cannot
  host an `AssetKind`-keyed API. §13's "shared editor infra" option applies. The §16 physical
  files still ship from `Hrot.AI.Behaviors`; `AssetRoots` only *names + resolves* those roots.
- **Root resolution:** resolve the roots to the directory where the §16 asset/recipe folders
  ship. Prefer resolving relative to the `Hrot.AI.Behaviors` assembly location
  (`typeof(<some Hrot.AI.Behaviors type>).Assembly.Location` → its directory) if
  `Hrot.Editor.AiShared` already references `Hrot.AI.Behaviors`; otherwise use
  `AppContext.BaseDirectory`. Both resolve to the same output directory at runtime. Document
  which you chose and why in the report.

### Scenario without `AssetKind.Scenario` (DEV-LEAD DECISION — follow exactly)
`AssetKind.Scenario` does **not** exist yet and is intentionally added later in MTB-P5-T2
(adding it now would ripple through 127 `AssetKind` usages/switches and violate this task's
"constants only" scope). So in this batch:
- Expose the Scenario recipe root as the dedicated member `ScenariosRecipesRoot`
  (= `<RecipesRoot>/Scenarios`).
- Do **not** add `AssetKind.Scenario`. Do **not** add a Scenario arm to `AssetsFor`.
- The success-condition tests below are adapted to this decision **without weakening the
  intent** (Scenario → `Recipes/Scenarios`; Scenario has no Assets root). MTB-P5-T2 will fold
  Scenario into `RecipesFor` once the enum value exists.

## Tests required — file: `Hrot/Editor/Hrot.Editor.AiShared.Tests/.../AssetRootsTests.cs` (NEW)
(`Hrot.Editor.AiShared.Tests` already references both `Hrot.Editor.AiShared` and
`Hrot.AI.Behaviors`.) Each test must assert the **actual returned path value** (verify it ends
with / contains the expected relative segment using `Path`-normalized comparison, so it passes
on Windows `\` separators):

- `AssetsFor_EachFileKind_ReturnsExpectedRelativeSegment` — Blueprint→`Assets/Blueprints`,
  Hsm→`Assets/HSMs`, BTree→`Assets/BTrees`.
- `RecipesFor_AllKinds_IncludingScenario` — Blueprint→`Recipes/Blueprints`, Hsm→`Recipes/HSMs`,
  BTree→`Recipes/BTrees`, **and** `ScenariosRecipesRoot`→`Recipes/Scenarios`.
- `AssetsFor_Scenario_HasNoAssetsRoot` — assert there is no Assets root for scenarios: `AssetsFor`
  has no Scenario arm and `ScenariosRecipesRoot` is the only scenario root (document the
  null/throw contract for unsupported kinds; assert `AssetsFor(AssetKind.Blackboard)` /
  `AssetsFor(AssetKind.Utility)` throw `ArgumentOutOfRangeException`).
- `AssetsRoot_And_RecipesRoot_AreDisjoint` — `AssetsRoot` and `RecipesRoot` are different
  directories and neither is a subpath of the other (the §16 disjoint-roots invariant).

Use the path comparison style of existing AiShared tests; assertions must check real values, not
string presence in code.

## Hard constraints
- Do NOT delete or modify legacy/assembly-loading code (assembly contributors,
  `BTreeDefinition`/`HsmDefinition`, `AmbushTree`, `UrbanCombat`, Persistence-Unification migration).
- Do NOT move any asset/recipe files, change any `.csproj` glob, or repoint any consumer — those
  are MTB-P0-T2/T3.
- No refactors/renames outside adding `AssetRoots` + its test file.
- Keep public APIs of existing types unchanged.

## Definition of done (all required)
- `AssetRoots` added per the API + placement decisions above; XML-doc the public members.
- The four named tests added and passing, asserting real path values.
- `dotnet build IOS-IG-SimHost.sln` is green (TreatWarningsAsErrors is on — zero warnings).
- Test suite green **without** `BLUEPRINT_REGENERATE_SNAPSHOTS`:
  - `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"` → 0 failed.
  - Sanity-run the catalogued hot suites with the same filter:
    `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"`
    and `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests --filter "..."` → 0 failed.
  - Your new tests must pass **unfiltered**; do NOT add any `Stability` trait to dodge a failure.
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-01-REPORT.md` (template in DEV-GUIDE_claude.md §4):
  paste the actual test-run summaries (counts), state the resolution-base choice, and list every
  file you changed.

Do not weaken, skip, or auto-pass any test. If something cannot be done as specified, stop and
report why rather than stubbing it.
