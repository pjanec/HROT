# BATCH-02: Folder reorg — move files + repoint consumers to AssetRoots

> **⚠ RESUME NOTE (a prior run died on a transient connection error after T2).**
> Verify with `git status` first: **T2 is already DONE** — all `.bp.json/.hsm.json/.btree.json`
> files are already `git mv`'d into the new `Assets/*` and `Recipes/Blueprints` layout (staged as
> renames) and `Hrot.AI.Behaviors.csproj` globs are updated. **Do NOT move files again or re-edit
> the csproj** unless a glob is genuinely wrong. **T3 is entirely UNDONE and the build is currently
> BROKEN** (consumers still reference old paths). Do T3 in full + the entire Definition of done.
**Tasks:** MTB-P0-T2, MTB-P0-T3   **Phase:** 0 — Folder Reorganization   **Est:** ~12–16h
**Dependencies:** BATCH-01 (AssetRoots exists in `Hrot.Editor.AiShared/Identity/AssetRoots.cs`)

> **Do tasks in sequence. Do NOT start T3 until T2's moves + csproj globs are done and the
> solution still builds. Do NOT finish until the FULL suite is green at the end.** This is one
> batch on purpose (DEV-LEAD decision DEC-5): moving files without repointing consumers leaves
> the suite red, so both land together.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/main-toolbar-1/DESIGN.md` §16 (Folder Reorganization) — the target layout & rationale.
3. `.dev/_DONE/main-toolbar-1/TASK-DETAIL.md` → "MTB-P0-T2" and "MTB-P0-T3" — the acceptance bars.
4. `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetRoots.cs` — the authority you repoint to.

---

## Target layout (§16) — all under `Hrot/Subsystems/Hrot.AI.Behaviors/`
```
Assets/      (final assets; browse/save destination)
  Blueprints/   HSMs/   BTrees/
Recipes/     (creation sources; never browsed)
  Blueprints/   HSMs/   BTrees/   Scenarios/
```

## ⚠ CORRECTIVE (review round 2) — add the missing named T2 test
The named success-condition test `FolderLayoutTests.Output_HasAssetsAndRecipesRoots` was not
created. Add it now (DEV-LEAD decision DEC-8). The original wording assumes finals ship to
**output** under `Assets/<Kind>` — but reality is: final `.bp.json/.hsm.json/.btree.json` are
generator `AdditionalFiles` compiled into the assembly and are NOT copied to output; only recipes
(`Content`) ship to output `Recipes/Blueprints`. So assert the **achievable** invariants in a new
`FolderLayoutTests` (place in `Hrot.Blueprints.Tests/Editor/`, mirror how other tests resolve the
Behaviors project/output dir, e.g. `TestData`/assembly-location helpers):
- **(a) Output:** the build-output `Recipes/Blueprints` dir exists and contains the recipe
  templates (e.g. `CountingDemo.bp.json`).
- **(b) Source project:** the resolved Behaviors **source** dir has `Assets/Blueprints`,
  `Assets/HSMs`, `Assets/BTrees` (each with its moved file) and `Recipes/Blueprints` (with the
  recipe templates).
- **(c) No leftovers:** no `*.bp.json` remains directly under a bare `Blueprints/` folder (neither
  a top-level `Blueprints/*.bp.json` nor `Blueprints/Recipes/`), and no `Machines/`/`Trees/` files.
Do NOT assert finals in the output tree (they are compiled in, not copied). Keep all other tests
and the existing `AssetScanTests`/`DiscoverRecipesTests` intact. Re-run the suites; everything that
passed must still pass and the new `FolderLayoutTests` must pass unfiltered.

## Task T2 — Move files + update `.csproj` globs (MTB-P0-T2)

### Moves (use `git mv` so history is preserved; **do not** edit file contents)
- `Blueprints/*.bp.json`  → `Assets/Blueprints/`   (the 5 top-level blueprints incl. `Count4.bp.json`)
- `Blueprints/Recipes/*`  → `Recipes/Blueprints/`  (the recipe `.bp.json` templates)
- `Machines/*`            → `Assets/HSMs/`         (`SampleGuard.hsm.json`)
- `Trees/*`               → `Assets/BTrees/`        (`SampleScout.btree.json`)
- Create empty-but-shipped `Recipes/Scenarios/` only if §16 needs a seed root to exist; otherwise
  leave it for later phases (do NOT invent scenario files).
- After moving, the old `Blueprints/`, `Machines/`, `Trees/` source folders should no longer
  contain these files. Remove now-empty dirs.

### `Hrot.AI.Behaviors.csproj` glob updates (currently lines ~70–90)
Repoint every glob to the new layout so the source generator still sees inputs and the right
files ship to `bin`:
- `AdditionalFiles Include="Blueprints\**\*.bp.json" Exclude="Blueprints\Recipes\*.bp.json"`
  → final blueprints now under `Assets\Blueprints\**\*.bp.json` (no Recipes to exclude — recipes
  moved out of the Assets tree).
- `UpToDateCheckInput` for `.bp.json` → same new path.
- `AdditionalFiles Include="Trees\**\*.btree.json"` → `Assets\BTrees\**\*.btree.json`.
- `AdditionalFiles Include="Machines\**\*.hsm.json"` → `Assets\HSMs\**\*.hsm.json`.
- `Content Include="Blueprints\Recipes\*.bp.json"` (CopyToOutputDirectory) → `Recipes\Blueprints\*.bp.json`,
  preserving the `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>` and the output
  relative path so recipes ship under `Recipes/Blueprints/` in `bin` (use `<Link>` if needed to
  control the output subpath).
- Ensure final assets that must ship to `bin` for output-dir scanners ALSO copy (if they did
  before): verify whether `*.bp.json`/`*.hsm.json`/`*.btree.json` finals are expected in `bin`
  under `Assets/<Kind>/`. If a scanner reads them from output, add the corresponding `Content`
  copy entries under the new `Assets\<Kind>\` paths. Confirm against how the contributors resolve
  their scan root (see T3).

### T2 success conditions
- `dotnet build IOS-IG-SimHost.sln` green after the move + glob update (zero new warnings —
  TreatWarningsAsErrors is on in `Hrot.AI.Behaviors`).
- New test `FolderLayoutTests.Output_HasAssetsAndRecipesRoots` (place in the most appropriate
  existing `*.Tests` project that already runs against the Behaviors build output — e.g.
  `Hrot.Blueprints.Tests` or `Hrot.Editor.AiShared.Tests`): enumerate the build output dir and
  assert (a) `Assets/Blueprints`, `Assets/HSMs`, `Assets/BTrees` exist with their files, (b)
  `Recipes/Blueprints` exists with the recipe templates, (c) NO `*.bp.json` remains directly under
  a bare `Blueprints/` output folder.
- `git status` shows renames (R), not delete+add of changed content.

## Task T3 — Repoint consumers to AssetRoots (MTB-P0-T3)

### First, extend `AssetRoots` with relative-segment helpers (DEV-LEAD decision DEC-6)
`AssetRoots`' existing properties resolve absolute **output** paths via `AppContext.BaseDirectory`.
Some consumers operate on the **source project dir** (resolved from the `.csproj`) and cannot use
those absolute paths. To keep `AssetRoots` the single authority for the §16 segment names, add:
- `string AssetsRelative(AssetKind kind)` → `"Assets/Blueprints"` | `"Assets/HSMs"` | `"Assets/BTrees"` (throws AOORE for rootless kinds).
- `string RecipesRelative(AssetKind kind)` → `"Recipes/Blueprints"` | `"Recipes/HSMs"` | `"Recipes/BTrees"`.
- `string ScenariosRecipesRelative` → `"Recipes/Scenarios"`.
Implement the absolute properties (`AssetsFor`/`RecipesFor`/`ScenariosRecipesRoot`) in terms of
these relatives (`Path.Combine(AppContext.BaseDirectory, AssetsRelative(kind))`) so segment names
live in exactly one place. Add unit tests for the new relative helpers
(`AssetRootsTests.AssetsRelative_*`, `RecipesRelative_*`) asserting the literal segments.

### Repoint OUTPUT-dir consumers → `AssetRoots` absolute props
These resolve paths from the assembly/output location:
- `BlueprintEditorBootstrap.DiscoverRecipes()` (`Hrot.Blueprints.Editor/BlueprintEditorBootstrap.cs`
  ~L157–175): replace `Path.Combine(assemblyLocation, "Blueprints", "Recipes")` with the recipe
  root for Blueprints under output (`AssetRoots.RecipesFor(AssetKind.Blueprint)` — confirm it equals
  output `Recipes/Blueprints`). Keep `SearchOption.AllDirectories`.
- `BlueprintAssetContributor` / any `*JsonAssetContributor` final-asset scan roots: scan finals
  from `AssetRoots.AssetsFor(<kind>)` ONLY (so recipes under `Recipes/…` are never returned by a
  final-asset scan). Find these via the codebase-memory MCP / grep for the contributors.

### Repoint PROJECT-dir consumers → resolved project dir + `AssetRoots.*Relative(...)`
These resolve the source `.csproj` dir (so writes persist to version control) — do NOT force the
output-based absolute props here; combine the project dir with the relative helper:
- `EditorSubsystem.cs` recipe-save (~L2038–2058), quick-reload (~L2494–2513), and the
  `Path.Combine(aiRootDir, "Blueprints"|"Trees"|"Machines")` blocks (~L641–657): replace the
  literal `"Blueprints"`, `"Blueprints/Recipes"`, `"Trees"`, `"Machines"` segments with
  `AssetRoots.AssetsRelative(...)` / `AssetRoots.RecipesRelative(...)` as appropriate to the new
  layout. (Recipe **save** target → `Recipes/Blueprints`; final blueprint dir → `Assets/Blueprints`;
  HSM JSON → `Assets/HSMs`; BTree JSON → `Assets/BTrees`.) Preserve the project-dir resolution
  mechanism (`ResolveAiBehaviorsDir`/`AiBehaviorsProjectPath`).
- `RecipeCreateModal.cs` uses `DiscoverRecipes()` — no path change needed if Bootstrap is fixed.

### Repoint TESTS with hardcoded pre-move paths (DEV-LEAD decision DEC-7 — NOT weakening)
Update the fixture paths these tests build so they track the moved files (keep all assertions):
- `Hrot.Blueprints.Tests/Debug/CF7rev_EndToEndTests.cs` (L61/265/319), `CF2_AuthoredIdProbeTests.cs`
  (L52), `CF1_NodeIdentityDiagnosticsTests.cs` (L62): `…/Hrot.AI.Behaviors/Blueprints/Count4.bp.json`
  → `…/Hrot.AI.Behaviors/Assets/Blueprints/Count4.bp.json`.
- `Hrot.Blueprints.Tests/Host/FixedStringPinTests.cs` (L165) & `Integration/WhenNodeEditorSmokeTest.cs`
  (L148) & `Compiler/RecipeIntegrityTests.cs` (L16/27): `Blueprints/Recipes` → `Recipes/Blueprints`.
- `Hrot.Hsm.Editor.Tests/SampleGuardDiscoveryTests.cs` (L79): `Machines/SampleGuard.hsm.json` →
  `Assets/HSMs/SampleGuard.hsm.json`.
- `Hrot.BTree.Editor.Tests/SampleScoutDiscoveryTests.cs` (L74): `Trees/SampleScout.btree.json` →
  `Assets/BTrees/SampleScout.btree.json`.
- `Hrot.AiEditor.Generators.Tests/Equivalence/MigrationEquivalenceTests.cs` (L461/505): `Trees/…`,
  `Machines/…` → `Assets/BTrees/…`, `Assets/HSMs/…`.
Prefer routing these through `AssetRoots.*Relative(...)` where they build a relative segment, so a
future move only touches `AssetRoots`.

### T3 success conditions (new tests)
- `DiscoverRecipesTests.Discovers_FromRecipesBlueprintsRoot` — `DiscoverRecipes()` returns the
  recipe templates from the new `Recipes/Blueprints` root (assert a known recipe name is present).
- `AssetScanTests.RecipesExcludedFromFinalScan` — a recipe placed under `Recipes/Blueprints` is
  NOT returned by the final-asset (Assets/Blueprints) scan; a final under `Assets/Blueprints` IS.
- Grep-clean: no string literal `"Blueprints/Recipes"`, bare `"Machines"`, bare `"Trees"`, or
  bare `"Blueprints"` segment left in the scanned production code paths — all go via `AssetRoots`.

## Hard constraints
- Do NOT delete/modify legacy/assembly-loading code (assembly contributors,
  `BTreeDefinition`/`HsmDefinition`, `AmbushTree`, `UrbanCombat`, Persistence-Unification migration).
- Do NOT change file CONTENTS of any moved `.json` (moves only).
- No scope creep beyond moves, csproj globs, the listed consumer repoints, the `AssetRoots`
  relative helpers, and the listed test path updates.
- Do NOT weaken/skip/auto-pass tests or add a Stability trait to dodge a failure. Fix root causes.

## Definition of done (all required)
- `dotnet build IOS-IG-SimHost.sln` green (zero new warnings).
- Run the FULL suite WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`. If any snapshot/golden test
  touches these blueprints, re-run clean to get the true baseline; fix root causes, do not regen.
  At minimum these must be 0-failed with `--filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"`:
  `Hrot.Blueprints.Tests`, `Hrot.Editor.AiShared.Tests`, `Hrot.Hsm.Editor.Tests`,
  `Hrot.BTree.Editor.Tests`, `Hrot.AiEditor.Generators.Tests`, `Fdp.Toolkits.Tests`,
  `Hrot.SimHost.Tests`. Your NEW tests must pass UNFILTERED.
- Write `.dev/_DONE/main-toolbar-1/reports/BATCH-02-REPORT.md`: list every file moved (old→new), every
  consumer repointed (file:line), the new `AssetRoots` helpers added, every test path updated,
  paste actual test-run summary counts, and answer the insight questions (issues, weak points,
  decisions beyond spec, edge cases — esp. anything about source-generator inputs or snapshots).

If something cannot be done as specified, stop and report why rather than stubbing it.
