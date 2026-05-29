# BATCH-22 Report

**Date:** 2025-07-29  
**Batch file:** `.dev/json-migration/batches/BATCH-22-INSTRUCTIONS.md`  
**Status:** COMPLETE

---

## Tasks Completed

### Task 1 — D-034: Remove dead-code null-conditional in EntityPatch.AddField

**File:** `Hrot/Engine/Hrot.Common/Scenario/Migrations/Helpers/EntityPatch.cs`

Removed the `?.` null-conditional operator from `component[targetName] = defaultValue?.DeepClone();`,
replacing it with `component[targetName] = defaultValue.DeepClone();`.

The `ArgumentNullException` guard added in D-026 (BATCH-21) already guarantees `defaultValue` is
non-null before the assignment, so the null-conditional was unreachable dead code. Removing it makes
the code read consistently with the invariant the guard enforces.

**Verification:** 56/56 tests in `Hrot.Common.Tests` pass.

---

### Task 2 — D-035: Add corpus tests for minimal-entity and empty-entities

**File:** `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/Phase3MigratorTests.cs`

Added two new test methods after Test 19 (`V1ToV2_Then_V2ToV1_EntityInfoName_SurvivesRoundTrip`):

- **Test 20** — `V1MinimalEntity_MigratedThroughPipeline_MatchesV2MinimalEntity`: migrates
  `test-data/scenario-corpus/multi-version/v1_minimal-entity/scenario.json` to v2 and asserts
  structural equality with the pre-authored v2 baseline.

- **Test 21** — `V1EmptyEntities_MigratedThroughPipeline_MatchesV2EmptyEntities`: migrates
  `test-data/scenario-corpus/multi-version/v1_empty-entities/scenario.json` to v2 and asserts
  structural equality with the pre-authored v2 baseline.

Both tests use the existing `FindWorkspaceRoot()`, `BuildServices()`, and `NormalizeForComparison()`
helpers (runtime metadata fields stripped before comparison).

**Verification:** Total test count increased from 54 to 56; all 56 pass.

---

### Task 3 — D-021: Remove SubsystemType and SchemaVersion from Header class

**File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Assets/GraphTypes.cs`

Removed `SubsystemType` and `SchemaVersion` properties from the `Header` sealed class. These fields
were superseded by the `$meta` envelope (JSON doc-type and schema version) introduced in Phase 2.
A comment explains the removal.

**Call-site audit:**
- `Hrot.Blueprints.Compiler.csproj` — builds with no errors.
- `Hrot.Blueprints.Compiler.Tests.csproj` — builds with no errors. The one remaining match
  (`meta.SchemaVersion` in `BlueprintJsonServicesTests.cs`) refers to `JsonEnvelope.Read()` output,
  not the removed `Header` field.
- `Hrot.Blueprints.Tests.csproj` — this project was already broken (CS0234/CS0246 for missing
  `Hrot.Editor` namespace) and does NOT reference `Hrot.Blueprints.Compiler`. Its test-helper
  builders reference `Hrot.Blueprints.Core.Assets.Header` but the Compiler assembly is not in scope,
  so these files were already failing before the change. No new errors were introduced.

**Full-solution build:** No new errors beyond pre-existing CS0234/CS0246 in `Hrot.Blueprints.Tests`.

---

## Issues Encountered

None. All three tasks were clean, low-risk changes.

## Weak Points Spotted

- `Hrot.Blueprints.Tests` project is a long-term problem: it references `Hrot.Blueprints.Editor`
  but does not have `Hrot.Blueprints.Compiler` as a dependency, and the `Header` class lives in
  the Compiler assembly. Test helper files in that project (BlueprintAssetBuilder, Stage1, Stage4,
  etc.) that construct `Header { SubsystemType = ..., SchemaVersion = ... }` are effectively dead
  code in that project. They should be cleaned up when the Editor dependency is restored or the
  project is restructured.

## Design Decisions

- Left the empty `Header` class body with a comment rather than removing it entirely. The property
  was removed but the class itself is still referenced by `BlueprintAsset.Header` elsewhere; removing
  the class would require a larger refactor outside the D-021 scope.
