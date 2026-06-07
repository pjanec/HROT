# BATCH-21 Completion Report

## Summary

All 6 tasks completed successfully.

| Task | Status |
|------|--------|
| D-019: Document sync-wrapper decision in 05-integration-patches.md | PASS |
| D-026: Add null guard to EntityPatch.AddField + test T_EP_13 | PASS |
| D-027: Add TransformComponent tests T_EP_14/15/16 | PASS |
| D-028: Add InferCasing tests T_EP_17/18/19/20 | PASS |
| JM-P5-001: Add minimal-entity and empty-entities corpus pairs | PASS |
| JM-P5-004: Stale-sidecar audit | CLEAN |

---

## Test Results

### Before

`Hrot.Common.Tests`: 34/34 pass (no EntityPatch tests for TransformComponent/InferCasing/null guard)

### After

`Hrot.Common.Tests`: 54/54 pass (8 new tests: T_EP_13 through T_EP_20)

`Hrot.Editor.Tests`: 114/114 pass (unchanged)

`Fdp.Toolkits.Tests` (EX_T + InlineArray + Defaults_MatchDesign): 31/31 pass (unchanged)

---

## Files Changed

### Modified

- `.dev/json-migration/05-integration-patches.md`
  - Key Finding 5: updated to record that both `RoadNetworkLoader.LoadFromJson` and `NodeConfiguration.LoadFrom` use `.GetAwaiter().GetResult()` sync wrapper (option b). Documents why option (a) async was rejected. Future-work note added.
  - `NodeConfiguration.LoadFrom` section: updated call-site pseudo-code to reflect the actual synchronous implementation.

- `Hrot/Engine/Hrot.Common/Scenario/Migrations/Helpers/EntityPatch.cs`
  - Added `ArgumentNullException` guard as first statement in `AddField(root, componentName, fieldName, JsonNode defaultValue, ...)`.

- `Hrot/Engine/Hrot.Common.Tests/Scenario/Migrations/EntityPatchTests.cs`
  - Added T_EP_13: null default value throws `ArgumentNullException`.
  - Added T_EP_14/15/16: `TransformComponent` tests (mutates component, skips absent component, adds sibling component).
  - Added T_EP_17/18/19/20: `InferCasing` tests via `AddField` with `CasingPolicy.MatchExisting` (all-Pascal, all-camel, empty, tie).

- `test-data/scenario-corpus/BASELINES.md`
  - Added Corpus Inventory table listing all 6 scenario files (original 2 plus 4 new).

### Created

- `test-data/scenario-corpus/multi-version/v1_minimal-entity/scenario.json`
- `test-data/scenario-corpus/multi-version/v2_minimal-entity/scenario.json`
- `test-data/scenario-corpus/multi-version/v1_empty-entities/scenario.json`
- `test-data/scenario-corpus/multi-version/v2_empty-entities/scenario.json`

---

## JM-P5-004 Stale-Sidecar Audit

**Result: CLEAN**

Searched the entire workspace for:
- Directories named `.migration-snapshots` or `.migration-journals`
- Files matching `*.migration-*.json`

No matches found. The `PersistentMigrationAdapter` pruning is working correctly; no stale sidecars have accumulated.

---

## Developer Insights

### Issues Encountered

1. **`InferCasing` is private** -- the test instructions said "test via `AddField` with `CasingPolicy.MatchExisting`" which is the correct approach. No issues with the indirect testing strategy.

2. **EntityPatchTests `weight` field** -- In T_EP_20, the initial draft used `["weight"] = 10` directly, but `JsonObject` stores `JsonNode` values. Using `JsonValue.Create(10)` or just `10` (implicitly converted) both work, but `JsonValue.Create(10)` was used for consistency.

3. **Empty-entities migration** -- The v2 corpus file for empty entities has `"schemaVersion": 2` with empty `entities: {}`. This is correct: the migrator runs but processes zero entities, resulting in a document that is structurally identical to the v1 except for the schema version.

### Weak Points Spotted

- `EntityPatch.AddField`'s `defaultValue?.DeepClone()` used a null-conditional even after the guard is added, making it redundant. The guard was added as a first statement; the `?.` is now dead code but harmless. A follow-up cleanup could change it to `defaultValue.DeepClone()` after the guard.

- The minimal-entity and empty-entities corpus files are standalone fixtures with no dedicated test method (the existing tests only load `v1_complete`/`v2_complete`). A future batch should add T4/T5 corpus tests that cover the new pairs (e.g., `V1MinimalEntity_MigratedThroughPipeline_MatchesV2MinimalEntity`).

### Design Decisions

- Chose to keep `InferCasing` private and test it entirely through the public API (`AddField` + `CasingPolicy.MatchExisting`). This is lower coupling: the test doesn't depend on the implementation's internal structure, only on the observable output.
- Added four separate `MatchExisting` tests instead of a single table-driven theory, matching the style of the existing tests in `EntityPatchTests.cs`.
