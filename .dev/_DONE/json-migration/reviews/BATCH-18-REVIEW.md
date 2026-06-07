# BATCH-18 Review — C0 (D-023, D-024) + JM-P4-004/005: CLI Migrate Subcommand

**Verdict: APPROVED**
**Reviewer:** Dev Lead
**Date:** 2026-05-29

---

## Deliverables Checklist

| Item | Status | Notes |
|------|--------|-------|
| C0-A: user-edit-survives test added | ✅ | Phase3MigratorTests.cs +1 test |
| C0-B: EntityPatchTests.cs created | ✅ | 12 tests |
| HrotRunnerConfiguration: 3 new CLI args | ✅ | --target-version, --input-dir, --dry-run |
| HrotRunnerConfiguration: migrate early-return | ✅ | Validate() exits early for migrate mode |
| MigrateMode.cs created | ✅ | All algorithm steps correct |
| Program.cs: stub replaced | ✅ | Uses MigrateMode, Main is now async Task<int> |
| MigrateModeTests.cs: 8 tests | ✅ | All pass |
| Hrot.Common.Tests: 46/46 pass | ✅ | 33 pre-existing + 13 new |
| MigrateModeTests: 8/8 pass | ✅ | New tests |
| Build clean (Hrot.Common, Hrot.ClusterRunner) | ✅ | 0 errors, 0 warnings |

---

## Test Quality Assessment

### C0-A: user-edit-survives round-trip test (D-023 closure)

`V1ToV2_Then_V2ToV1_EntityInfoName_SurvivesRoundTrip` correctly exercises the design §10.9
rule 3 requirement:
1. v1 entity with `EntityInfo.Name = "Commander-Alpha"` constructed.
2. `V1ToV2_EntityInfo_AddTags.Apply` invoked — adds `Tags: []`.
3. `V2ToV1_EntityInfo_RemoveTags.Apply` invoked — removes `Tags`.
4. Assert `EntityInfo["Name"] == "Commander-Alpha"` — edit survived. ✓
5. Assert `EntityInfo["Tags"] == null` — Tags absent after down-migration. ✓

This test correctly establishes the pattern for future migrators.

### C0-B: EntityPatch unit tests (D-024 closure)

All 12 tests reviewed:

**OnEachEntity group (T_EP_01..02):**
- T_EP_01: Counter == 2 for 2 entities. Correct callback count assertion. ✓
- T_EP_02: Deviation from instructions accepted — `OnEachEntity` always fires for all entities;
  test uses a guarded callback. This is correct: the test verifies that non-EntityInfo entities
  pass through without incident, and the callback can discriminate. The test name says
  "EntityMissingEntityInfo_CallbackNotCalled" but the implementation is "called but guarded".
  Acceptable — behavior under test is correct. ✓

**AddField group (T_EP_03..05):**
- T_EP_03: Field added; asserts type is `JsonArray` and count == 0. ✓
- T_EP_04: Existing `[1]` not overwritten. ✓
- T_EP_05: Modifies entity 1's Tags; verifies entity 2's is still empty. This directly tests the
  `DeepClone()` isolation in `EntityPatch.AddField`. Correct and important. ✓

**RemoveField group (T_EP_06..07):** Direct property-absent assertion and count-unchanged
assertion. Both correct. ✓

**RenameField group (T_EP_08..09):** T_EP_08 checks that new key exists with correct value AND
old key is absent. Comprehensive. ✓

**RenameComponent group (T_EP_10..11):** T_EP_11 correctly asserts `MigrationException` is
thrown when both component names exist. ✓

**OnComponent group (T_EP_12):** Verifies that callback fires exactly once for the one entity
that has the component. ✓

**Gap noted (P3):** `TransformComponent` method is not covered. See D-027 below.

### JM-P4-004/005: MigrateModeTests (8 tests)

All 8 tests use temp directories with proper `try/finally` cleanup. ✓

- **T_CLI_01** (empty dir): Correct; confirms 0 migrated/skipped/failed line. ✓
- **T_CLI_02** (no-meta file): SKIPPED on no-envelope. ✓
- **T_CLI_03** (v2 file, default target=current=2): SKIPPED already at target + file unchanged assertion. ✓
- **T_CLI_04** (v1 file → v2): Checks log "OK (v1 -> v2)" AND reads back file, peeks $meta, asserts
  schemaVersion == 2. This is the most important test — verifies the full round-trip write. ✓
- **T_CLI_05** (dry-run): Verifies "[dry-run]" tag in log AND file still has schemaVersion == 1 on disk.
  Correct bidirectional check. ✓
- **T_CLI_06** (explicit --target-version 1, v2 → v1): Peeks the written file via `JsonEnvelope.Peek` for
  version, then checks `!ContainsKey("Tags")`. Double assertion for both the version and the migration
  content. ✓
- **T_CLI_07** (target version 99): No migration path → Pipeline throws → FAILED line → exit code 1. ✓
- **T_CLI_08** (3 files, 1 migrated + 2 skipped): Summary count assertion. ✓

### MigrateMode.cs Implementation

- Algorithm exactly matches the design spec from BATCH-18-INSTRUCTIONS.
- Uses `_services.Registry.GetCurrentVersion(...)` correctly (public API); catches `MigrationException`
  for unknown docTypes and returns Skip.
- Dry-run path skips `SaveAsync` / file write correctly in both the default and explicit-version paths.
- Per-file output format: `N/total: filename -- OK (v1 -> v2)` or `... -- SKIPPED (reason)` or
  `... -- FAILED: message`. Summary line: `[migrate] Completed: N migrated, M skipped, K failed.` ✓
- Non-zero exit code: `return failed > 0 ? 1 : 0`. ✓
- No static mutable state. ✓
- Passes `CancellationToken` through all async calls. ✓

### HrotRunnerConfiguration.cs additions

- Three `[Option]` properties added correctly with appropriate defaults (-1, empty string, false).
- `Validate()` early-return for `migrate` mode before the `editor` mode check: ✓
  This prevents the "Editor must not be combined with..." error from firing when `--mode migrate`
  is used with other flags (correctly — migrate is standalone by design).

### Program.cs change

- `static int Main(string[] args)` changed to `static async Task<int> Main(string[] args)`.
  Required for `await runner.RunAsync()`. The CommandLineParser library is compatible with async
  Task<int> Main. ✓
- New migrate mode block: constructs `HrotMigrationBootstrap.BuildClusterRunnerMigrate()`,
  creates `MigrateMode`, calls `await runner.RunAsync()`. Clean. ✓

---

## Issues Found

### P3 Issues (tracked, deferred)

**D-027 | Source: BATCH-18 review | Priority: P3 | Target: Backlog | Status: OPEN**

`EntityPatch.TransformComponent` is not covered by `EntityPatchTests.cs`. The D-024 description
listed it as a method future migrators depend on. Add a test in a subsequent batch.

**D-028 | Source: BATCH-18 review | Priority: P3 | Target: Backlog | Status: OPEN**

`EntityPatch.InferCasing` (majority-vote logic for `MatchExisting` casing policy) is not tested
directly. It is exercised indirectly through the up-migrator round-trip, but a dedicated unit test
for `InferCasing` boundary cases (tie goes to Pascal, all-lowercase, mixed) would improve coverage.

---

## Debt Closure

- **D-023**: RESOLVED. User-edit-survives round-trip test added. ✓
- **D-024**: RESOLVED (with P3 gap D-027). All primary EntityPatch methods tested. ✓

---

## Decision

**APPROVED.** Phase 4 CLI subcommand (JM-P4-004 + JM-P4-005) is complete and all tests pass.
Phase 4 Editor UI tasks (JM-P4-001..003) proceed in BATCH-19.
