# BATCH-04 Review

**Batch:** BATCH-04
**Reviewer:** Dev Lead
**Status:** APPROVED ✅

---

## Review Summary

All 7 tasks (CS013, CS014, CS017, CS018, CS019, CS026, CS027) are fully implemented, built, and tested.

---

## Correctness Review

### CS013 — UnitSubordinateTranslator
- Extract correctly writes `CommanderNetworkId` from `UnitSubordinate.Commander` via `NetworkEntityMap`
- Inject correctly creates `InitialUnitSubordinateIntent` for deferred resolution post-load
- Registered in `HrotScenarioSerializerFactory`
- **PASS**

### CS014 — GenesisMaterializationSystem
- Normal resolution: atomic two-write (UnitSubordinate + UnitRoster) — correct
- Deferred retry: commander absent → intent retained (entity must be `Constructing`, not `Active`)
- Capacity check: drops intent with warn log on overflow
- Escape hatch: `Active` lifecycle with no commander → drops intent with warn log
- All 4 test cases aligned with TASK-DETAIL success conditions
- **PASS**

### CS017 + CS018 + CS019 — ORBAT UI
- Both copies of `OrbatNodeViewModel`, `IOrbatController`, `SharedOrbatPanel` updated consistently
- `InternalsVisibleTo` already set for test assemblies
- Stub implementations in adapters are appropriate for this batch (full implementation is CS020/CS021)
- **PASS**

### CS026 — Drain guard
- Guard pattern (`foreach ... return`) correctly blocks `DrainDeferredAcks` while intents exist
- Both `HrotScenarioLoadHandler` and `CgfScenarioLoadHandler` updated consistently
- **PASS**

### CS027 — StagingEntityExtractor remap
- `CommanderNetworkId` correctly remapped from staging entity's network ID to new ID on load
- Consistent with other remap blocks in `StagingEntityExtractor`
- **PASS**

---

## Test Quality

- All new tests follow Arrange/Act/Assert pattern
- Test fixtures properly register managed components before use
- CS014-T02 uses `SetLifecycleState(Constructing)` appropriately to distinguish retry from escape-hatch
- No test duplication

---

## Decision

**APPROVED** — proceed to BATCH-05 (CS020, CS021, CS024, CS025).
