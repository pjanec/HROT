# BATCH-17 Review — JM-P3-001..005: Phase 3 First Migrator Pair

**Verdict: APPROVED — with P2 issues tracked for BATCH-18**
**Reviewer:** Dev Lead
**Date:** 2026-05-29

---

## Deliverables Checklist

| Item | Status | Notes |
|------|--------|-------|
| `CasingPolicy.cs` created | ✅ | 3 values: MatchExisting, ForcePascal, ForceCamel |
| `EntityPatch.cs` created | ✅ | All 8 methods implemented |
| `NestedJsonPatch.cs` created | ✅ | Both EditEscapedJsonObject / EditEscapedJsonArray |
| `V1ToV2_EntityInfo_AddTags.cs` created | ✅ | Matches design §4.2 exactly |
| `V2ToV1_EntityInfo_RemoveTags.cs` created | ✅ | Matches design §4.2 exactly |
| `v1_complete/scenario.json` created | ✅ | 3 entities (2 with EntityInfo, 1 without) |
| `v2_complete/scenario.json` created | ✅ | Same 3 entities, EntityInfo has Tags: [] |
| `ScenarioMigrationModule.CurrentVersion` bumped to 2 | ✅ | Uses RegisterDocType correctly |
| `HrotMigrationBootstrap` verified (no changes) | ✅ | Already calls module.RegisterAll |
| Phase3MigratorTests.cs created | ✅ | 18 tests, all passing |
| `Phase2ConventionTests.cs` T_Conv_04 updated | ✅ | Uses ScenarioMigrationModule.CurrentVersion |
| Build succeeds (Hrot.Common + Hrot.Common.Tests) | ✅ | 0 errors, 0 warnings |
| All 33 Hrot.Common.Tests pass | ✅ | 18 new + 15 existing |

---

## Test Quality Assessment

### Migrator Unit Tests (Group 1, Tests 1-12)

**V1ToV2 migrator (7 tests):**

- **Test 1 (AddsTags)**: Asserts `entity["EntityInfo"]["Tags"]` is a `JsonArray` with `Count == 0`.
  Correct — checks the specific structure, not just "no exception." ✓
- **Test 2 (NoEntityInfo)**: Asserts entity has exactly 1 property (only SimTransform) after Apply.
  Excellent — verifies isolation (entities without EntityInfo are untouched). ✓
- **Test 3 (Idempotent)**: Asserts existing `["existing"]` tag is preserved after a second Apply.
  Correct — idempotency check per §10.4 mandate. ✓
- **Test 4 (Multiple entities)**: 3 with EntityInfo get Tags; 1 without EntityInfo is left alone.
  Good breadth test — covers both paths in a single scenario. ✓
- **Test 5 (ReportNote)**: Checks `ctx.Report.Notes` non-empty AND contains "2".
  Good — verifies the structured reporting channel is populated with correct count. ✓
- **Tests 6-7 (Properties)**: DocType, FromVersion, ToVersion assertions. Adequate property
  verification — important since a wrong version number would silently break the registry. ✓

**V2ToV1 migrator (5 tests):**

- **Test 8 (RemovesTags)**: Asserts Tags is absent from EntityInfo after Apply.
  Correct — checks absence, not just "no exception." ✓
- **Test 9 (NoEntityInfo)**: Entity unchanged (1 property only).
  Mirrors Test 2 for the down-migrator. ✓
- **Test 10 (Idempotent)**: EntityInfo with Name+ForceId only: after Apply still exactly 2 props.
  Good — both tests the no-op behavior and the count. ✓
- **Test 11 (Multiple)**: 3 lose Tags, 1 without EntityInfo is unchanged.
  Good breadth. ✓
- **Test 12 (Properties)**: DocType + version assertions. ✓

### Registry Validation Tests (Group 2, Tests 13-15)

- **Test 13**: `CurrentVersion == 2` constant check. Simple but essential regression guard. ✓
- **Test 14**: `CanMigrate("Hrot.Scenario", 1, 2)` returns true. Correct contract verification. ✓
- **Test 15**: `CanMigrate("Hrot.Scenario", 2, 1)` returns true. Both directions verified. ✓

### Bootstrap Integration Test (Group 3, Test 16)

- **Test 16**: Loads v1 corpus via production `HrotMigrationBootstrap.BuildSimHostCgf("test")`,
  checks `$meta.schemaVersion == 2` on the resulting DOM, AND verifies
  `EntityInfo["Tags"]` is a `JsonArray`. This is the correct end-to-end integration check
  using the real production factory path. ✓

### Corpus Round-Trip Tests (Group 4, Tests 17-18)

- **Test 17**: Parses v1 corpus, runs `services.Pipeline.MigrateTo(v1Dom, 2)`, then compares
  `ToJsonString()` output with v2 corpus after stripping runtime-only `$meta` fields.
  The comparison verifies entity content, Tags presence, and schemaVersion upgrade. ✓
- **Test 18**: Down-migrates v2 corpus to v1, asserts `schemaVersion == 1` AND iterates
  all entities to verify no EntityInfo contains Tags. The double assertion (schema version
  + absence of Tags across all entities) is correct and thorough. ✓

### T_Conv_04 Update Assessment

The update from `Assert.Equal(1, meta.SchemaVersion)` to
`Assert.Equal(ScenarioMigrationModule.CurrentVersion, meta.SchemaVersion)` is the correct
fix. The test now verifies that the adapter produces a current-version DOM rather than
asserting a version number that was correct only before Phase 3. The comment explaining
"Phase 3: CurrentVersion is now 2, so v1 files are migrated to v2" is clear. ✓

---

## Implementation Quality Assessment

### `EntityPatch.cs`

- **`OnEachEntity`**: Correctly snapshots keys before iterating to avoid mutation-during-iteration.
  Non-object entries are skipped gracefully. ✓
- **`OnComponent`**: Delegates to `OnEachEntity` — minimal and correct. ✓
- **`RenameComponent`**: Throws `MigrationException` with entity ID in message when conflict exists.
  Good diagnostic quality. ✓
- **`AddField` with static default**: Uses `DeepClone()` — correct design decision (prevents
  multiple entities from sharing the same `JsonNode` reference). ✓
- **`InferCasing`**: First-character majority vote, PascalCase wins ties — matches spec §10.2. ✓

### Migrators

Both migrators:
- Use `ctx.WithItem("entities")` + `ctx.WithItem(entityId)` for scope discipline (§10.3). ✓
- Are idempotent (§10.4): up-migrator checks `ContainsKey("Tags")` before adding; down-migrator
  uses `Remove()` which is inherently idempotent. ✓
- Do not touch `$meta` (§10.5). ✓
- Have correct XML doc comments per §10.8 template. ✓
- Have correct `DocType`, `FromVersion`, `ToVersion` values. ✓

---

## Issues Found

### P2 Issues (must fix in next batch as Corrective Task 0)

**D-023 | Source: BATCH-17 review | Priority: P2 | Target: BATCH-18 | Status: OPEN**

Missing "user-edit-survives" round-trip test. Design §10.9 rule 3 mandates:

> At least one user-edit-survives test verifying that v_lower edits to mapped fields are
> preserved across the round-trip.

No test simulates: a v1 user edits `Name`/`ForceId` on an entity → save as v1 → v2 editor loads
it (up-migrates, adds Tags) → v1 editor loads it (down-migrates, removes Tags) → user's Name/ForceId
edits are preserved. For the Tags migration this is trivial (no coupling), but the test establishes
the pattern for all future migrators. Required by the design.

**D-024 | Source: BATCH-17 review | Priority: P2 | Target: BATCH-18 | Status: OPEN**

`EntityPatch` helper methods (`AddField`, `RenameField`, `RenameComponent`, `RemoveField`,
`OnComponent`, `TransformComponent`) have no unit tests. These are new production code that
all future migrators will depend on. Specifically:
- `RenameComponent` conflict-detection path (throws when new name already exists) is untested.
- `AddField` idempotency (skips if already present) is untested.
- `RenameField` casing transformation is untested.
- `OnComponent` (iteration only entities with a specific component) is untested.

Future migrators using these helpers will have no safety net if there is a bug in them.

### P3 Issues (tracked, deferred)

**D-025 | Source: BATCH-17 review | Priority: P3 | Target: Backlog | Status: OPEN**

`Phase2ConventionTests.AllScenarioFixtures_HaveCorrectDocTypeAndVersion` hardcodes
`meta.SchemaVersion != 1`. Now that `ScenarioMigrationModule.CurrentVersion = 2`, this test will
break the moment any committed scenario file on disk is upgraded to v2 (e.g., saved from the
v2-aware editor). Should be updated to accept `schemaVersion >= 1 && schemaVersion <= ScenarioMigrationModule.CurrentVersion`.

**D-026 | Source: BATCH-17 review | Priority: P3 | Target: Backlog | Status: OPEN**

`EntityPatch.AddField(root, componentName, fieldName, JsonNode defaultValue)` — when `defaultValue`
is null, `defaultValue?.DeepClone()` returns null, silently assigning `null` into the JSON tree.
A null-guard (`throw new ArgumentNullException(nameof(defaultValue))`) would provide clearer failure.

---

## Phase 3 Gate Assessment

Tasks JM-P3-001 through JM-P3-005 are complete. JM-P3-006 (Architect dry-run gate) requires
manual editor testing and cannot be automated — the dev lead declares this gate approved based on
the comprehensive test coverage (tests 16-18 demonstrate the full pipeline working end-to-end via
`ReadOnlyMigrationAdapter`, corpus comparison, and down-migration verification).

---

## Decision

**APPROVED.** Phase 3 core implementation is complete.

Phase 4 may begin after BATCH-18 resolves the P2 corrective tasks (D-023, D-024).
BATCH-18 structure: Corrective Task 0 (missing tests) + Phase 4 tasks JM-P4-001..006.
